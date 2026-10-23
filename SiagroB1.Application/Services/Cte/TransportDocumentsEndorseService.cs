using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Averbação manual do CT-e autorizado na AT&amp;M: login com as credenciais da filial e envio do cteProc.
/// Falha da AT&amp;M não lança: grava o motivo em <c>EndorsementError</c> e o documento segue sem averbação.
/// </summary>
public class TransportDocumentsEndorseService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    IAverbaClient averba)
{
    public const string NotAuthorized = "Só CT-e autorizado pode ser averbado.";
    public const string AlreadyEndorsed = "Este CT-e já foi averbado.";
    public const string NotConfigured = "Configure a averbação AT&M na filial (Transportadora).";

    public async Task<AverbaTestResult> EndorseAsync(Guid key, string user, CancellationToken ct = default)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await db.Context.TransportDocuments.Include(x => x.Xmls).FirstOrDefaultAsync(x => x.Key == key, ct)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Authorized)
            return Refuse(NotAuthorized);
        if (doc.EndorsementStatus != CteEndorsementStatus.None)
            return Refuse(AlreadyEndorsed);

        var settings = await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == doc.BranchCode, ct);
        if (!AverbaLogin.IsConfigured(settings))
            return Refuse(NotConfigured);

        var xml = doc.Xmls.FirstOrDefault(x => x.Kind == CteXmlKind.Authorized)?.Xml;
        if (xml is null)
            return await FailAsync(doc, user, "O XML autorizado do CT-e não foi encontrado.");

        var login = await AverbaLogin.LoginAsync(averba, configuration, settings!, ct);
        if (!login.Success || string.IsNullOrWhiteSpace(login.Bearer))
            return await FailAsync(doc, user, login.Error ?? "Falha no login da AT&M.");

        var sent = await averba.SendCteAsync(settings!.AverbaEnvironment, login.Bearer, xml, ct);
        if (!sent.Success)
            return await FailAsync(doc, user, sent.Error ?? "A AT&M recusou a averbação.");

        doc.EndorsementStatus = CteEndorsementStatus.Endorsed;
        doc.EndorsementProtocol = Cut(sent.Protocol, 40);
        doc.EndorsedAt = ParseDate(sent.AverbadoAt);
        doc.EndorsementError = null;
        doc.UpdatedAt = DateTime.Now;
        doc.UpdatedBy = user;
        foreach (var i in sent.Insurances)
            db.Context.TransportDocumentEndorsements.Add(new TransportDocumentEndorsement
            {
                TransportDocumentKey = doc.Key,
                EndorsementNumber = Cut(i.EndorsementNumber, 40),
                InsurerTaxId = Cut(i.InsurerTaxId, 14),
                InsurerName = Cut(i.InsurerName, 115),
                PolicyNumber = Cut(i.PolicyNumber, 40),
                Amount = ParseAmount(i.Amount),
                Branch = Cut(i.Branch, 40),
                MovementType = Cut(i.MovementType, 20),
            });
        await db.SaveChangesAsync();

        return new AverbaTestResult { Success = true, Message = $"CT-e averbado. Protocolo {doc.EndorsementProtocol}." };
    }

    private static AverbaTestResult Refuse(string message) => new() { Success = false, Message = message };

    private async Task<AverbaTestResult> FailAsync(TransportDocument doc, string user, string error)
    {
        doc.EndorsementError = Cut(error, 500);
        doc.UpdatedAt = DateTime.Now;
        doc.UpdatedBy = user;
        await db.SaveChangesAsync();
        return new AverbaTestResult { Success = false, Message = error };
    }

    private static string? Cut(string? value, int max) => value is { } v && v.Length > max ? v[..max] : value;

    private static DateTime ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        || DateTime.TryParse(value, new CultureInfo("pt-BR"), DateTimeStyles.None, out d)
            ? d
            : DateTime.Now;

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
