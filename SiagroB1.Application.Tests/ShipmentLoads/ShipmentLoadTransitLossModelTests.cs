using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadTransitLossModelTests
{
    private static AppDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void EnumValues_AreAppendOnly()
    {
        Assert.Equal(3, (int)RefusalDestination.TransitLoss);
        Assert.Equal(18, (int)StorageTransactionType.TransitLoss);
        Assert.Equal(10, (int)ShipmentLoadStatus.TransitLoss);
        Assert.Equal(26, (int)ShipmentLoadMovementType.TransitLoss);
        Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues<TransitLossReason>().Select(v => (int)v));
    }

    [Theory]
    // total, faturado, devolvido, transbordado, quebra, esperado
    [InlineData(40_000, 25_000, 0, 0, 15_000, 0)]
    [InlineData(40_000, 10_000, 5_000, 5_000, 8_000, 12_000)]
    public void CalculateAvailableQuantity_SubtractsTheFiveTerms(
        decimal total, decimal invoiced, decimal returned, decimal transshipped, decimal loss, decimal expected) =>
        Assert.Equal(expected, ShipmentLoad.CalculateAvailableQuantity(total, invoiced, returned, transshipped, loss));

    [Fact]
    public void AvailableQuantity_UsesTransitLossQuantity()
    {
        var load = new ShipmentLoad
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 40_000,
            InvoicedQuantity = 25_000,
            TransitLossQuantity = 15_000,
        };

        Assert.Equal(0, load.AvailableQuantity);
    }

    [Fact]
    public void Model_PersistsTransitLossColumns()
    {
        using var context = ModelOnlyContext();

        var loss = context.Model.FindEntityType(typeof(ShipmentLoad))!
            .FindProperty(nameof(ShipmentLoad.TransitLossQuantity));
        Assert.NotNull(loss);
        Assert.Contains("DECIMAL(18,3)", loss!.GetColumnType(), StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(context.Model.FindEntityType(typeof(StorageTransaction))!
            .FindProperty(nameof(StorageTransaction.TransitLossReason)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ShipmentLoadRefusal))!
            .FindProperty(nameof(ShipmentLoadRefusal.TransitLossReason)));
    }
}
