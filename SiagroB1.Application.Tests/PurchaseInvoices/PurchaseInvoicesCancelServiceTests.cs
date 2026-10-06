using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

public class PurchaseInvoicesCancelServiceTests
{
    [Fact]
    public async Task Origin_with_an_active_purchase_return_cannot_be_cancelled()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var origin = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        scenario.Db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = origin.BranchCode, CardCode = origin.CardCode, InvoiceType = PurchaseInvoiceType.Return,
            IssuerType = DocumentIssuerType.Own, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Pending,
            PurchaseInvoiceOriginKey = origin.Key,
        });
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Documento de entrada possui devolução.", ex.Message);
    }

    [Fact]
    public async Task Cancelled_return_does_not_block_the_origin()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var origin = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        scenario.Db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = origin.BranchCode, CardCode = origin.CardCode, InvoiceType = PurchaseInvoiceType.Return,
            IssuerType = DocumentIssuerType.Own, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Cancelled,
            PurchaseInvoiceOriginKey = origin.Key,
        });
        await scenario.Db.SaveChangesAsync();

        await new PurchaseInvoicesCancelService(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Cancelled,
            (await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey)).InvoiceStatus);
    }

    [Fact]
    public async Task Cancel_after_nfe_requires_the_nfe_cancelled_and_skips_the_lock()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var service = new PurchaseInvoicesCancelService(scenario.Db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CancelAfterNfeAsync(scenario.InvoiceKey, "tester"));
        Assert.Equal("A NF-e deste documento não está cancelada na SEFAZ.", ex.Message);

        var invoice = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        invoice.NfeStatus = NfeStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();

        await service.CancelAfterNfeAsync(scenario.InvoiceKey, "tester");

        var saved = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.Equal("tester", saved.CanceledBy);
    }
}
