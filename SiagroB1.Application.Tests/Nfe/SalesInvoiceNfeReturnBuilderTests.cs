using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

public class SalesInvoiceNfeReturnBuilderTests
{
    private static SalesInvoiceNfeReturnBuilder Builder(NfeReturnScenario s) =>
        new(s.Sale.Db, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    [Fact]
    public async Task Builds_the_same_return_the_devolver_creates()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var origin = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);

        var built = await Builder(s).BuildAsync(
            origin, new Dictionary<Guid, decimal> { [s.SaleItemKey] = 10_000m }, "Texto livre", "tester");

        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0), built.InvoiceDate);
        Assert.Null(built.PaymentConditionCode);
        Assert.Null(built.DeliveryCardCode);
        Assert.Equal("Texto livre", built.Comments);
        Assert.Equal(s.ReturnUsageCode, built.Items.Single().UsageCode);
        Assert.Equal(10_000m, built.Items.Single().Quantity);
    }

    [Fact]
    public async Task Origin_without_return_usage_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos")).ReturnUsageCode = null;
        await s.Sale.Db.SaveChangesAsync();
        var origin = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Builder(s).BuildAsync(
            origin, new Dictionary<Guid, decimal> { [s.SaleItemKey] = 1m }, "x", "tester"));

        Assert.Contains("não tem natureza de devolução cadastrada", ex.Message);
    }

    [Fact]
    public async Task Reference_text_names_the_nfe_and_the_document()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var origin = await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey);

        Assert.Equal("Devolução da NF-e 1 série 1 (doc.saída 000002388).", SalesInvoiceNfeReturnBuilder.ReferenceText(origin));
    }
}
