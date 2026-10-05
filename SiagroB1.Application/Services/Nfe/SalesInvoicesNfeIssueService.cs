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
/// "Emitir NF-e" (spec §9.2). Na filial com a regra ativa, emitir é o que confirma o documento.
/// Ordem que não pode mudar: reservar o número e SALVAR; assinar e validar; gravar
/// "Em processamento" + XML assinado e SALVAR; só então enviar.
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
    }

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
