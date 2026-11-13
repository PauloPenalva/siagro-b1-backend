using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
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
        Assert.Equal(expected, ShipmentLoad.CalculateAvailableQuantity(total, invoiced, returned, transshipped, 0));
    }

    /// <summary>GAC-1176: o fato "terminou aqui" mora no transbordo, e nulo = não é destino final.</summary>
    [Fact]
    public void Transshipment_HasFinalDestinationAndTypeColumns()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoadTransshipment))!;

        Assert.True(entity.FindProperty(nameof(ShipmentLoadTransshipment.FinalDestinationAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(ShipmentLoadTransshipment.FinalDestinationBy))!.IsNullable);
        Assert.NotNull(entity.FindProperty(nameof(ShipmentLoadTransshipment.Type)));
        Assert.Null(entity.FindProperty(nameof(ShipmentLoadTransshipment.IsFinalDestination)));
    }

    [Fact]
    public void ShipmentLoad_HasIsInTransfer()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoad))!;
        Assert.NotNull(entity.FindProperty(nameof(ShipmentLoad.IsInTransfer)));
    }

    /// <summary>Persistidos como int: valor novo entra no fim, e linha antiga (0) é Transbordo.</summary>
    [Fact]
    public void NewEnumValues_KeepTheirNumbers()
    {
        Assert.Equal(0, (int)TransshipmentType.Transshipment);
        Assert.Equal(1, (int)TransshipmentType.Transfer);
        Assert.Equal(27, (int)ShipmentLoadMovementType.TransferCompleted);
        Assert.Equal(28, (int)ShipmentLoadMovementType.TransferReopened);
    }

    [Fact]
    public void IsFinalDestination_FollowsTheDate()
    {
        var transshipment = new ShipmentLoadTransshipment { WarehouseCode = "ARM99" };
        Assert.False(transshipment.IsFinalDestination);

        transshipment.FinalDestinationAt = DateTime.Now;
        Assert.True(transshipment.IsFinalDestination);
    }
}
