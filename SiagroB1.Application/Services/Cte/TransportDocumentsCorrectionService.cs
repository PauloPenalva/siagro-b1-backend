using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>CC-e estruturada do CT-e (evento 110110): só campos da lista fechada, sequência 1 a 20.</summary>
public class TransportDocumentsCorrectionService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    CteServiceContextFactory contexts,
    ICteSefazClient sefaz,
    Func<DateTimeOffset>? clock = null)
{
    public const int MaxSequence = 20;

    public const string NotAuthorized = "Só é possível corrigir CT-e autorizado.";
    public const string NoItems = "Informe ao menos um campo a corrigir.";
    public const string TooManyCorrections = "O CT-e já atingiu o limite de 20 cartas de correção.";

    public async Task<NfeSefazResult> SendCorrectionAsync(
        Guid key, IReadOnlyList<(string Group, string Field, string Value)> items, string user)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        if (items.Count == 0)
            throw new DefaultException(NoItems);

        var request = new List<CteCorrectionItem>();
        foreach (var (group, field, value) in items)
        {
            var allowed = CteCorrectableFields.Find(group, field)
                          ?? throw new DefaultException($"O campo {group}/{field} não pode ser corrigido por CC-e.");
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
                throw new DefaultException($"Informe o novo valor de {allowed.Label}.");
            if (text.Length > allowed.MaxLength)
                throw new DefaultException($"{allowed.Label} aceita até {allowed.MaxLength} caracteres.");
            request.Add(new CteCorrectionItem(allowed.Group, allowed.Field, text, null));
        }

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await db.Context.TransportDocuments.FirstOrDefaultAsync(x => x.Key == key)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Authorized || doc.AccessKey is null)
            throw new DefaultException(NotAuthorized);

        var last = await db.Context.TransportDocumentCorrections.Where(x => x.TransportDocumentKey == key)
            .MaxAsync(x => (int?)x.Sequence) ?? 0;
        if (last >= MaxSequence)
            throw new DefaultException(TooManyCorrections);
        var sequence = last + 1;

        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == doc.BranchCode)
                     ?? throw new DefaultException($"Filial {doc.BranchCode} não encontrada.");
        var settings = await db.Context.BranchCteSettings.AsNoTracking().FirstAsync(x => x.BranchCode == doc.BranchCode);
        using var context = await contexts.OpenAsync(doc.BranchCode, doc.Environment ?? settings.Environment);

        NfeEventResult result;
        try
        {
            result = await sefaz.SendCorrectionAsync(
                new CteCorrectionRequest(doc.AccessKey, NfeText.AlphaNumeric(branch.TaxId), sequence, request, now), context.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException("Sem resposta da SEFAZ na carta de correção: consulte o CT-e antes de tentar de novo.");
        }

        if (!NfeStatusCodes.IsEventRegistered(result.StatusCode))
            return new NfeSefazResult(result.StatusCode, result.Reason);

        db.Context.TransportDocumentCorrections.Add(new TransportDocumentCorrection
        {
            TransportDocumentKey = doc.Key,
            Sequence = sequence,
            ItemsJson = JsonSerializer.Serialize(request.Select(i => new { i.Group, i.Field, i.Value })),
            Protocol = result.Protocol,
            SentAt = result.RegisteredAt?.DateTime ?? now.DateTime,
        });
        if (result.ProcEventXml is not null)
            db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
            {
                TransportDocumentKey = doc.Key, Kind = CteXmlKind.Correction, Xml = result.ProcEventXml,
            });
        doc.UpdatedAt = DateTime.Now;
        doc.UpdatedBy = user;
        await db.SaveChangesAsync();

        return new NfeSefazResult(result.StatusCode, result.Reason);
    }
}
