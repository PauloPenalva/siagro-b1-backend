using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.StorageInvoices;

public class StorageInvoicesBillingGetTests
{
    [Fact]
    public async Task Returns_documents_receivables_and_balance()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var nfse = await seed.AddDocumentAsync(400m);
        await seed.AddDocumentAsync(100m, InvoiceStatus.Pending, TaxDocumentKind.Nfe, "DS2");
        await seed.AddDocumentAsync(300m, InvoiceStatus.Cancelled, number: "DS3");
        await seed.AddTitleAsync(nfse, 400m);

        var dto = await new StorageInvoicesBillingGetService(seed.Db).ExecuteAsync(seed.Invoice.Key);

        Assert.Equal(1000m, dto.TotalAmount);
        Assert.Equal(400m, dto.BilledAmount);     // só confirmados
        Assert.Equal(500m, dto.Balance);          // total − não cancelados (rascunho conta)
        Assert.Equal(3, dto.Documents.Count);
        var receivable = Assert.Single(dto.Receivables);
        Assert.Equal(400m, receivable.OpenAmount);
        Assert.Equal("DS1", receivable.InvoiceNumber);
    }

    [Fact]
    public async Task Cancelled_receivables_listed_with_status()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var doc = await seed.AddDocumentAsync(400m);
        await seed.AddTitleAsync(doc, 400m, FinancialDocumentStatus.Canceled);

        var dto = await new StorageInvoicesBillingGetService(seed.Db).ExecuteAsync(seed.Invoice.Key);

        Assert.Equal(FinancialDocumentStatus.Canceled, Assert.Single(dto.Receivables).Status);
    }

    [Fact]
    public async Task Unknown_key_throws_not_found()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new StorageInvoicesBillingGetService(seed.Db).ExecuteAsync(Guid.NewGuid()));
    }
}
