using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Cancela o CT-e autorizado (evento 110111). Só vale se registrado (135/155): então o documento vira Cancelado,
/// o procEventoCTe é guardado e o gancho da entrega B roda. Recusa da SEFAZ não muda o documento.
/// </summary>
public class TransportDocumentsCancelService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    CteServiceContextFactory contexts,
    ICteSefazClient sefaz,
    ICteCancellationHook hook,
    FinancialDocumentsTransportService financial,
    Func<DateTimeOffset>? clock = null)
{
    public const int MinJustificationLength = 15;
    public const int MaxJustificationLength = 255;
    public const int DeadlineHours = 168;

    public const string NotAuthorized = "Só é possível cancelar CT-e autorizado.";
    public const string InvalidJustification = "A justificativa do cancelamento tem de 15 a 255 caracteres.";
    public const string DeadlineExpired = "O prazo de cancelamento do CT-e (168 horas) acabou.";

    public async Task<NfeSefazResult> CancelAsync(Guid key, string? justification, string user)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        var reason = (justification ?? string.Empty).Trim();
        if (reason.Length is < MinJustificationLength or > MaxJustificationLength)
            throw new DefaultException(InvalidJustification);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await db.Context.TransportDocuments.Include(x => x.Xmls).FirstOrDefaultAsync(x => x.Key == key)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Authorized || doc.AccessKey is null || doc.Protocol is null)
            throw new DefaultException(NotAuthorized);

        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);
        if (doc.AuthorizedAt is { } authorizedAt && now.DateTime - authorizedAt > TimeSpan.FromHours(DeadlineHours))
            throw new DefaultException(DeadlineExpired);

        // CT-e em fatura de frete ativa: o valor já foi aglutinado; recusa ANTES da SEFAZ.
        var invoiceCode = await db.Context.FreightInvoiceItems.AsNoTracking()
            .Where(i => i.TransportDocumentKey == doc.Key && i.IsActive && i.Invoice!.Status != FreightInvoiceStatus.Cancelled)
            .Select(i => i.Invoice!.Code).FirstOrDefaultAsync();
        if (invoiceCode is not null)
            throw new DefaultException($"CT-e está na fatura {invoiceCode}; retire-o ou cancele a fatura antes.");

        // Título a receber já baixado: recusa ANTES da SEFAZ, sem mudar nada.
        if (ErpMode.IsStandalone(configuration))
            await financial.EnsureCanUndoAsync(doc.Key);

        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == doc.BranchCode)
                     ?? throw new DefaultException($"Filial {doc.BranchCode} não encontrada.");
        var settings = await db.Context.BranchCteSettings.AsNoTracking().FirstAsync(x => x.BranchCode == doc.BranchCode);
        using var context = await contexts.OpenAsync(doc.BranchCode, doc.Environment ?? settings.Environment);

        NfeEventResult result;
        try
        {
            result = await sefaz.CancelAsync(
                new CteCancelRequest(doc.AccessKey, doc.Protocol, reason, NfeText.AlphaNumeric(branch.TaxId), now), context.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException("Sem resposta da SEFAZ no cancelamento: consulte o CT-e antes de tentar de novo.");
        }

        if (!NfeStatusCodes.IsEventRegistered(result.StatusCode))
            return new NfeSefazResult(result.StatusCode, result.Reason);

        doc.Status = CteStatus.Cancelled;
        doc.CancellationProtocol = result.Protocol;
        doc.CancelledAt = result.RegisteredAt?.DateTime ?? now.DateTime;
        doc.CancellationReason = reason;
        doc.StatusCode = result.StatusCode.ToString();
        doc.StatusReason = result.Reason.Length > 500 ? result.Reason[..500] : result.Reason;
        doc.UpdatedAt = DateTime.Now;
        doc.UpdatedBy = user;
        if (result.ProcEventXml is not null)
            db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
            {
                TransportDocumentKey = doc.Key, Kind = CteXmlKind.Cancellation, Xml = result.ProcEventXml,
            });
        await db.SaveChangesAsync();

        await hook.OnCancelledAsync(doc);
        return new NfeSefazResult(result.StatusCode, result.Reason);
    }
}
