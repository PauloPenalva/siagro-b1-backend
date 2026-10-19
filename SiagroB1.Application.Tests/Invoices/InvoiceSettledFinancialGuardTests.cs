using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.Invoices;

/// <summary>
/// Documento de entrada/saída com título baixado (parcial ou integral) não pode ser excluído nem cancelado, qualquer que
/// seja o caminho do título até o documento: chave do documento, linha do documento ou vínculo da conciliação fiscal.
/// </summary>
public class InvoiceSettledFinancialGuardTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly Guid _invoiceKey = Guid.NewGuid();
    private readonly Guid _lineKey = Guid.NewGuid();

    private async Task SeedTitleAsync(decimal settled,
        FinancialDocumentStatus status = FinancialDocumentStatus.PartiallySettled,
        Guid? purchaseInvoiceKey = null, Guid? salesInvoiceKey = null, Guid? originKey = null)
    {
        _db.Context.FinancialDocuments.Add(new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000009",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = FinancialDocumentNature.Firm,
            Status = status,
            DueDate = DateTime.Today,
            NetAmount = 100m,
            SettledAmount = settled,
            PurchaseInvoiceKey = purchaseInvoiceKey,
            SalesInvoiceKey = salesInvoiceKey,
            OriginKey = originKey,
            OriginType = FinancialDocumentOrigin.PurchaseInvoice
        });
        await _db.Context.SaveChangesAsync();
    }

    private async Task SeedPurchaseLineAsync()
    {
        _db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem { Key = _lineKey, PurchaseInvoiceKey = _invoiceKey });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Purchase_refuses_title_linked_by_invoice_key()
    {
        await SeedTitleAsync(10m, purchaseInvoiceKey: _invoiceKey);

        var error = await Assert.ThrowsAsync<DefaultException>(
            () => InvoiceSettledFinancialGuard.EnsurePurchaseHasNoSettlementAsync(_db.Context, _invoiceKey, "123"));

        Assert.Equal("O documento 123 possui baixa financeira (título FN000009). Cancelamento não permitido.",
            error.Message);
    }

    [Fact]
    public async Task Purchase_refuses_title_linked_by_line()
    {
        await SeedPurchaseLineAsync();
        await SeedTitleAsync(100m, FinancialDocumentStatus.Settled, originKey: _lineKey);

        await Assert.ThrowsAsync<DefaultException>(
            () => InvoiceSettledFinancialGuard.EnsurePurchaseHasNoSettlementAsync(_db.Context, _invoiceKey, "123"));
    }

    [Fact]
    public async Task Purchase_refuses_title_linked_by_fiscal_reconciliation()
    {
        await SeedPurchaseLineAsync();
        var linkKey = Guid.NewGuid();
        _db.Context.PurchaseFiscalReconciliations.Add(new PurchaseFiscalReconciliation
        {
            Key = linkKey, PurchaseInvoiceItemKey = _lineKey, StorageTransactionKey = Guid.NewGuid()
        });
        await _db.Context.SaveChangesAsync();
        await SeedTitleAsync(5m, originKey: linkKey);

        await Assert.ThrowsAsync<DefaultException>(
            () => InvoiceSettledFinancialGuard.EnsurePurchaseHasNoSettlementAsync(_db.Context, _invoiceKey, "123"));
    }

    [Fact]
    public async Task Purchase_allows_unsettled_or_canceled_titles()
    {
        await SeedTitleAsync(0m, FinancialDocumentStatus.Open, purchaseInvoiceKey: _invoiceKey);
        await SeedTitleAsync(10m, FinancialDocumentStatus.Canceled, purchaseInvoiceKey: _invoiceKey);

        await InvoiceSettledFinancialGuard.EnsurePurchaseHasNoSettlementAsync(_db.Context, _invoiceKey, "123");
    }

    [Fact]
    public async Task Sales_refuses_title_linked_by_line()
    {
        _db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem { Key = _lineKey, SalesInvoiceKey = _invoiceKey, ItemCode = "SOJA", UnitOfMeasureCode = "KG" });
        await _db.Context.SaveChangesAsync();
        await SeedTitleAsync(10m, originKey: _lineKey);

        var error = await Assert.ThrowsAsync<DefaultException>(
            () => InvoiceSettledFinancialGuard.EnsureSalesHasNoSettlementAsync(_db.Context, _invoiceKey, "77"));

        Assert.Contains("possui baixa financeira", error.Message);
    }

    [Fact]
    public async Task Sales_refuses_title_linked_by_invoice_key()
    {
        await SeedTitleAsync(10m, salesInvoiceKey: _invoiceKey);

        await Assert.ThrowsAsync<DefaultException>(
            () => InvoiceSettledFinancialGuard.EnsureSalesHasNoSettlementAsync(_db.Context, _invoiceKey, "77"));
    }

    [Fact]
    public async Task Sales_allows_unsettled_titles()
    {
        await SeedTitleAsync(0m, FinancialDocumentStatus.Open, salesInvoiceKey: _invoiceKey);

        await InvoiceSettledFinancialGuard.EnsureSalesHasNoSettlementAsync(_db.Context, _invoiceKey, "77");
    }
}
