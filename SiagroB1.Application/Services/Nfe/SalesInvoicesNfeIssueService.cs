using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Transmitir NF-e" do documento de saída (spec 2026-10-02 §9.2; spec 2026-10-06). O documento Normal é transmitido
/// já Confirmado e com tipo NF-e; a devolução própria e o complemento de ICMS (spec 2026-10-18 S7) continuam Pendentes —
/// neles, emitir é o que confirma.
/// Ordem que não pode mudar: reservar o número e SALVAR; assinar e validar; gravar "Em processamento" + XML assinado e
/// SALVAR; só então enviar.
/// </summary>
public class SalesInvoicesNfeIssueService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    NfeReadinessValidator readiness,
    BranchNfeSettingsService settingsService,
    NfeNumberReservationService reservation,
    INfeSefazClient sefaz,
    SalesInvoiceNfeResultHandler resultHandler,
    NfeOptions options,
    ILogger<SalesInvoicesNfeIssueService> logger,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
    : NfeIssueServiceBase<SalesInvoice>(
        db, gate, new SalesInvoiceNfeStore(db), settingsService, reservation, sefaz, resultHandler, options, logger, clock, storageZone)
{
    private readonly IUnitOfWork _db = db;

    protected override void EnsureIssuableType(SalesInvoice invoice)
    {
        // Spec 2026-10-18 S7: o complemento de ICMS sai como NF-e complementar (finNFe=2).
        if (invoice.InvoiceType != SalesInvoiceType.Normal && !SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice) &&
            !SalesInvoicesTaxApplyService.IsIcmsComplement(invoice))
            throw new DefaultException(
                "Só o documento Normal, a devolução criada pelo Devolver e o complemento de ICMS são emitidos como NF-e por aqui.");

        if (!invoice.IsNfeReturn && invoice.TaxDocumentKind == TaxDocumentKind.Nfse)
            throw new DefaultException("Documento do tipo NFS não é transmitido como NF-e.");

        if (!invoice.IsNfeReturn && invoice.TaxDocumentKind != TaxDocumentKind.Nfe)
            throw new DefaultException("Documento do tipo Outro não é transmitido como NF-e.");
    }

    protected override InvoiceStatus RequiredStatus(SalesInvoice invoice) =>
        IssuedPending(invoice) ? InvoiceStatus.Pending : InvoiceStatus.Confirmed;

    /// <summary>Devolução própria e complemento de ICMS: emitidos Pendentes; o resultado autorizado os confirma.</summary>
    private static bool IssuedPending(SalesInvoice invoice) =>
        invoice.IsNfeReturn || SalesInvoicesTaxApplyService.IsIcmsComplement(invoice);

    protected override string DateFixHint(SalesInvoice invoice) => IssuedPending(invoice)
        ? "altere a data e salve (os impostos são recalculados)."
        : "estorne a confirmação, altere a data e salve (os impostos são recalculados).";

    protected override DateTime? DocumentDate(SalesInvoice invoice) => invoice.InvoiceDate;

    protected override IReadOnlyList<INfeTaxedLine> Lines(SalesInvoice invoice) => invoice.Items.Cast<INfeTaxedLine>().ToList();

    // Devolução: o saldo da venda ANTES de reservar o número — a conferência da confirmação roda
    // depois da autorização, tarde demais para impedir uma NF-e de quantidade a mais.
    protected override async Task EnsureBeforeReservationAsync(SalesInvoice invoice)
    {
        // Complemento de ICMS: a original recarregada (V5) e a base/ICMS de toda linha (V3/V4), antes de numerar.
        if (SalesInvoicesTaxApplyService.IsIcmsComplement(invoice))
        {
            await SalesInvoiceIcmsComplement.EnsureIssuableAsync(_db.Context, invoice);
            return;
        }

        if (!invoice.IsNfeReturn)
            return;

        // Peso do cabeçalho x itens: a confirmação o exige e roda depois da autorização.
        SalesInvoicesReturnWeightService.EnsureHeaderWeightMatchesItems(invoice);
        await SalesInvoiceNfeReturnBalance.EnsureWithinAsync(_db.Context, invoice, invoice.Items);
    }

    /// <summary>Devolução simbólica do beneficiamento: NFref para a entrada do mesmo romaneio (spec 2026-10-12 §4.3).</summary>
    protected override async Task<NfeIssueContext> ValidateReadinessAsync(SalesInvoice invoice)
    {
        var context = await readiness.ValidateAsync(invoice);
        var custodyKey = await StorageCustodyGuard.ProcessingEntryAccessKeyAsync(_db.Context, invoice);

        return custodyKey is null ? context : context with { CustodyReferencedKey = custodyKey };
    }

    protected override NfeIssueInput BuildInput(
        SalesInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible) =>
        NfeIssueInputAssembler.Build(invoice, context, issuedAt, technicalResponsible);
}
