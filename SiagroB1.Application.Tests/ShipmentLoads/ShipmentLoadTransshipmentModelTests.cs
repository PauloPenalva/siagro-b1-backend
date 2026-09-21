using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadTransshipmentModelTests
{
    private static AppDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Transshipment_MapsToItsOwnTable()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoadTransshipment))!;
        Assert.Equal("SHIPMENT_LOAD_TRANSSHIPMENTS", entity.GetTableName());
    }

    [Fact]
    public void AllForeignKeys_AreNoAction()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoadTransshipment))!;
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }

    [Fact]
    public void StorageTransaction_HasTransshipmentKey()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(StorageTransaction))!;
        Assert.NotNull(entity.FindProperty(nameof(StorageTransaction.ShipmentLoadTransshipmentKey)));
    }

    [Fact]
    public void ShipmentLoad_HasTransshippedQuantity()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoad))!;
        Assert.NotNull(entity.FindProperty(nameof(ShipmentLoad.TransshippedQuantity)));
    }

    [Theory]
    // total, invoiced, returned, transshipped, esperado
    [InlineData(30, 0, 0, 0, 30)]
    [InlineData(30, 30, 0, 0, 0)]
    [InlineData(30, 0, 0, 30, 0)]
    [InlineData(59.5, 29.5, 0, 30, 0)]
    public void CalculateAvailableQuantity_SubtractsTheFourTerms(
        decimal total, decimal invoiced, decimal returned, decimal transshipped, decimal expected)
    {
        Assert.Equal(expected, ShipmentLoad.CalculateAvailableQuantity(total, invoiced, returned, transshipped));
    }
}
