using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleaseAvailableQuantityTests
{
    private static ShipmentRelease New(decimal released, decimal shipped, ReleaseStatus status = ReleaseStatus.Actived) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = Guid.NewGuid(),
        DeliveryLocationCode = "01",
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
    };

    [Fact]
    public void AvailableQuantity_DerivesFromShippedQuantity_WithoutTransactions()
    {
        var sr = New(released: 100m, shipped: 40m);
        Assert.Empty(sr.Transactions);
        Assert.Equal(60m, sr.AvailableQuantity);
    }

    [Fact]
    public void AvailableQuantity_NegativeShipped_FromNetReturn_IncreasesAvailable()
    {
        // shipped 80 − returned 30 = 50 usados
        var sr = New(released: 100m, shipped: 50m);
        Assert.Equal(50m, sr.AvailableQuantity);
    }

    [Fact]
    public void AvailableQuantity_Cancelled_IsZero()
    {
        var sr = New(released: 100m, shipped: 40m, status: ReleaseStatus.Cancelled);
        Assert.Equal(0m, sr.AvailableQuantity);
    }
}
