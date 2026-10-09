using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadRefusalModelTests
{
    private static AppDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Refusal_maps_to_its_own_table_with_noaction_fks()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoadRefusal))!;

        Assert.Equal("SHIPMENT_LOAD_REFUSALS", entity.GetTableName());
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }

    [Fact]
    public void Only_one_pending_refusal_per_load()
    {
        using var context = ModelOnlyContext();
        var index = context.Model.FindEntityType(typeof(ShipmentLoadRefusal))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(ShipmentLoadRefusal.ShipmentLoadKey)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[Status] = 0", index.GetFilter());
    }

    [Fact]
    public void Sales_invoice_points_to_the_refusal_with_noaction()
    {
        using var context = ModelOnlyContext();
        var fk = context.Model.FindEntityType(typeof(SalesInvoice))!
            .GetForeignKeys()
            .Single(f => f.Properties.Single().Name == nameof(SalesInvoice.ShipmentLoadRefusalKey));

        Assert.Equal(typeof(ShipmentLoadRefusal), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
    }

    [Fact]
    public void Enum_values_are_appended()
    {
        Assert.Equal(9, (int)ShipmentLoadStatus.RefusalPending);
        Assert.Equal(25, (int)ShipmentLoadMovementType.RefusalCancelled);
    }

    [Fact]
    public void Creation_never_keeps_a_refusal_key_from_the_body()
    {
        var invoice = new SalesInvoice { CardCode = "C1", ShipmentLoadRefusalKey = Guid.NewGuid() };

        SalesInvoiceNfeLock.ResetIssuanceFields(invoice);

        Assert.Null(invoice.ShipmentLoadRefusalKey);
    }

    [Fact]
    public async Task Patch_cannot_change_the_refusal_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var original = Guid.NewGuid();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceType = SalesInvoiceType.Return,
            InvoiceStatus = InvoiceStatus.Pending, ShipmentLoadRefusalKey = original,
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.ShipmentLoadRefusalKey = Guid.NewGuid();
        SalesInvoiceNfeLock.RestoreIssuanceFields(db.Context.Entry(invoice));

        Assert.Equal(original, invoice.ShipmentLoadRefusalKey);
    }
}
