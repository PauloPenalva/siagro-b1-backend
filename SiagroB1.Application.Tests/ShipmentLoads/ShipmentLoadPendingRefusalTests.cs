using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadPendingRefusalTests
{
    [Fact]
    public async Task Lists_the_returns_of_the_pending_refusal()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents: 2);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, 5_000m)).ToList(),
                RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse, "Recusado"),
            "tester");

        var rows = await new ShipmentLoadsPendingRefusalService(s.Db).ExecuteAsync(s.Load.Key);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Warehouse", r.Destination);
            Assert.Equal("ARMAZEM RETAGUARDA", r.DestinationWarehouseName);
            Assert.Equal("Recusado", r.Reason);
            Assert.Equal(5_000m, r.Quantity);
            Assert.Equal("Pending", r.InvoiceStatus);
            Assert.Equal("None", r.NfeStatus);
        });
    }

    [Fact]
    public async Task Cancelled_returns_are_left_out()
    {
        // O 2b cancelou a NF-e de uma das devoluções; a outra segue pendente e a recusa também.
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents: 2);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, 5_000m)).ToList(),
                RefusalDestination.Rebilling, null, "Recusado"),
            "tester");
        var cancelled = await s.Db.Context.SalesInvoices
            .Where(i => i.InvoiceType == SalesInvoiceType.Return).OrderBy(i => i.InvoiceNumber).FirstAsync();
        cancelled.InvoiceStatus = InvoiceStatus.Cancelled;
        await s.Db.SaveChangesAsync();

        var rows = await new ShipmentLoadsPendingRefusalService(s.Db).ExecuteAsync(s.Load.Key);

        Assert.Single(rows);
        Assert.NotEqual(cancelled.Key.ToString(), rows[0].SalesInvoiceKey);
    }

    [Fact]
    public async Task Empty_without_a_pending_refusal()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();

        Assert.Empty(await new ShipmentLoadsPendingRefusalService(s.Db).ExecuteAsync(s.Load.Key));
    }

    [Fact]
    public void Edm_declares_the_function_and_the_action()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        Assert.Single(model.SchemaElements.OfType<IEdmFunction>(), f => f.Name == "ShipmentLoadsGetPendingRefusal");
        Assert.Single(model.SchemaElements.OfType<IEdmAction>(), a => a.Name == "ShipmentLoadsCancelRefusal");
    }
}
