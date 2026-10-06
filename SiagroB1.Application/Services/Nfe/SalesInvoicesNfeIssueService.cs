using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
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
/// já Confirmado e com tipo NF-e; a devolução própria continua Pendente — nela, emitir é o que confirma.
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
        if (invoice.InvoiceType != SalesInvoiceType.Normal && !SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            throw new DefaultException(
                "Só o documento Normal e a devolução criada pelo Devolver são emitidos como NF-e por aqui.");

        if (!invoice.IsNfeReturn && invoice.TaxDocumentKind != TaxDocumentKind.Nfe)
            throw new DefaultException("Documento do tipo Outro não é transmitido como NF-e.");
    }

    protected override InvoiceStatus RequiredStatus(SalesInvoice invoice) =>
        invoice.IsNfeReturn ? InvoiceStatus.Pending : InvoiceStatus.Confirmed;

    protected override string DateFixHint(SalesInvoice invoice) => invoice.IsNfeReturn
        ? "altere a data e salve (os impostos são recalculados)."
        : "estorne a confirmação, altere a data e salve (os impostos são recalculados).";

    protected override DateTime? DocumentDate(SalesInvoice invoice) => invoice.InvoiceDate;

    protected override IReadOnlyList<INfeTaxedLine> Lines(SalesInvoice invoice) => invoice.Items.Cast<INfeTaxedLine>().ToList();

    // Devolução: o saldo da venda ANTES de reservar o número — a conferência da confirmação roda
    // depois da autorização, tarde demais para impedir uma NF-e de quantidade a mais.
    protected override async Task EnsureBeforeReservationAsync(SalesInvoice invoice)
    {
        if (!invoice.IsNfeReturn)
            return;

        // Peso do cabeçalho x itens: a confirmação o exige e roda depois da autorização.
        SalesInvoicesReturnWeightService.EnsureHeaderWeightMatchesItems(invoice);
        await SalesInvoiceNfeReturnBalance.EnsureWithinAsync(_db.Context, invoice, invoice.Items);
    }

    protected override Task<NfeIssueContext> ValidateReadinessAsync(SalesInvoice invoice) => readiness.ValidateAsync(invoice);

    protected override NfeIssueInput BuildInput(
        SalesInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible) =>
        NfeIssueInputAssembler.Build(invoice, context, issuedAt, technicalResponsible);
}
