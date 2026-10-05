using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Emitir NF-e" do Documento de Entrada de emissão própria (spec §8.3): a entrada Normal sai como NF-e de
/// ENTRADA (finalidade 1) e a devolução de compra como NF-e de SAÍDA (finalidade 4). Emitir é o que confirma.
/// </summary>
public class PurchaseInvoicesNfeIssueService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    NfeReadinessValidator readiness,
    BranchNfeSettingsService settingsService,
    NfeNumberReservationService reservation,
    INfeSefazClient sefaz,
    PurchaseInvoiceNfeResultHandler resultHandler,
    NfeOptions options,
    ILogger<PurchaseInvoicesNfeIssueService> logger,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
    : NfeIssueServiceBase<PurchaseInvoice>(
        db, gate, new PurchaseInvoiceNfeStore(db), settingsService, reservation, sefaz, resultHandler, options, logger, clock, storageZone)
{
    private readonly IUnitOfWork _db = db;

    protected override void EnsureIssuableType(PurchaseInvoice invoice)
    {
        if (invoice.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException("Só o documento de emissão própria é emitido como NF-e.");

        if (invoice.InvoiceType != PurchaseInvoiceType.Normal && !PurchaseInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            throw new DefaultException(
                "Só a entrada Normal e a devolução criada pelo Devolver são emitidas como NF-e por aqui.");
    }

    // Devolução de compra: o saldo da entrada ANTES de reservar o número — a SEFAZ autorizaria uma devolução
    // maior que a compra.
    protected override async Task EnsureBeforeReservationAsync(PurchaseInvoice invoice)
    {
        if (invoice.IsNfeReturn)
            await PurchaseInvoiceNfeReturnBalance.EnsureWithinAsync(_db.Context, invoice, invoice.Items);
    }

    protected override DateTime? DocumentDate(PurchaseInvoice invoice) => invoice.IssueDate;

    protected override IReadOnlyList<INfeTaxedLine> Lines(PurchaseInvoice invoice) => invoice.Items.Cast<INfeTaxedLine>().ToList();

    protected override Task<NfeIssueContext> ValidateReadinessAsync(PurchaseInvoice invoice) => readiness.ValidateAsync(invoice);

    protected override NfeIssueInput BuildInput(
        PurchaseInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible) =>
        NfeIssueInputAssembler.Build(invoice, context, issuedAt, technicalResponsible);

    /// <summary>O total da nota própria é o vNF (soma dos itens): o "valor declarado" passa a ser o emitido.</summary>
    protected override void BeforeSigning(PurchaseInvoice invoice) =>
        invoice.TotalDocumentValue = invoice.Items.Sum(i => i.Total);
}
