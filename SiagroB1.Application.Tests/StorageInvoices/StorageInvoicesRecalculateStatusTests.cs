using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.StorageInvoices;

/// <summary>Spec 2026-10-19 parte 3: situação da fatura derivada dos documentos de saída e dos títulos.</summary>
public class StorageInvoicesRecalculateStatusTests
{
    private static async Task<StorageInvoiceStatus> RecalculateAsync(StorageInvoiceBillingSeed seed)
    {
        await StorageInvoicesRecalculateStatusService.RecalculateAsync(seed.Db.Context, seed.Invoice.Key);
        await seed.Db.Context.SaveChangesAsync();
        return (await seed.Db.Context.StorageInvoices.AsNoTracking().SingleAsync(i => i.Key == seed.Invoice.Key)).Status;
    }

    [Fact]
    public async Task No_documents_is_open()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        Assert.Equal(StorageInvoiceStatus.Open, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task Draft_documents_keep_open()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await seed.AddDocumentAsync(1000m, InvoiceStatus.Pending);
        Assert.Equal(StorageInvoiceStatus.Open, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task Confirmed_below_total_is_partially_invoiced()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await seed.AddDocumentAsync(400m);
        Assert.Equal(StorageInvoiceStatus.PartiallyInvoiced, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task Confirmed_equal_total_is_invoiced()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var a = await seed.AddDocumentAsync(400m);
        var b = await seed.AddDocumentAsync(600m, kind: TaxDocumentKind.Nfe, number: "DS2");
        await seed.AddTitleAsync(a, 400m, FinancialDocumentStatus.Settled);
        await seed.AddTitleAsync(b, 600m);
        Assert.Equal(StorageInvoiceStatus.Invoiced, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task All_titles_settled_is_paid()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var a = await seed.AddDocumentAsync(1000m);
        await seed.AddTitleAsync(a, 500m, FinancialDocumentStatus.Settled);
        await seed.AddTitleAsync(a, 500m, FinancialDocumentStatus.Settled);
        Assert.Equal(StorageInvoiceStatus.Paid, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task Cancelled_titles_ignored_for_paid()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var a = await seed.AddDocumentAsync(1000m);
        await seed.AddTitleAsync(a, 1000m, FinancialDocumentStatus.Settled);
        await seed.AddTitleAsync(a, 1000m, FinancialDocumentStatus.Canceled);
        Assert.Equal(StorageInvoiceStatus.Paid, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task Invoiced_without_titles_stays_invoiced()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await seed.AddDocumentAsync(1000m);
        Assert.Equal(StorageInvoiceStatus.Invoiced, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task Legacy_paid_without_documents_stays_paid()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync(status: StorageInvoiceStatus.Paid);
        Assert.Equal(StorageInvoiceStatus.Paid, await RecalculateAsync(seed));
    }

    [Fact]
    public async Task Cancelled_invoice_stays_cancelled()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync(status: StorageInvoiceStatus.Cancelled);
        await seed.AddDocumentAsync(1000m);
        Assert.Equal(StorageInvoiceStatus.Cancelled, await RecalculateAsync(seed));
    }
}
