using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Uma liberação cancelada COM movimentação continua consumindo do contrato
/// exatamente o que foi romaneado — só o saldo não romaneado volta ao contrato.
/// </summary>
public class PurchaseContractShipmentReleaseConsumptionTests
{
    private static PurchaseContract NewContract(decimal totalVolume) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
    };

    private static ShipmentRelease NewRelease(decimal released, decimal shipped, ReleaseStatus status) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = Guid.NewGuid(),
        DeliveryLocationCode = "01",
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
    };

    [Fact]
    public void CancelledWithMovement_ReturnsOnlyUnshippedBalanceToContract()
    {
        var pc = NewContract(1000m);
        pc.ShipmentReleases.Add(NewRelease(1000m, 300m, ReleaseStatus.Cancelled));

        Assert.Equal(300m, pc.TotalShipmentReleases);
        Assert.Equal(700m, pc.TotalAvailableToRelease);
    }

    [Fact]
    public void CancelledWithoutMovement_ReturnsFullQuantityToContract()
    {
        var pc = NewContract(1000m);
        pc.ShipmentReleases.Add(NewRelease(1000m, 0m, ReleaseStatus.Cancelled));

        Assert.Equal(0m, pc.TotalShipmentReleases);
        Assert.Equal(1000m, pc.TotalAvailableToRelease);
    }

    [Fact]
    public void ActiveRelease_ConsumesFullReleasedQuantity()
    {
        var pc = NewContract(1000m);
        pc.ShipmentReleases.Add(NewRelease(1000m, 300m, ReleaseStatus.Actived));

        Assert.Equal(1000m, pc.TotalShipmentReleases);
        Assert.Equal(0m, pc.TotalAvailableToRelease);
    }

    [Fact]
    public void WithoutProvisioning_ExcludesPending_IncludesCancelledMovement()
    {
        var pc = NewContract(1000m);
        pc.ShipmentReleases.Add(NewRelease(200m, 0m, ReleaseStatus.Pending));
        pc.ShipmentReleases.Add(NewRelease(400m, 0m, ReleaseStatus.Actived));
        pc.ShipmentReleases.Add(NewRelease(300m, 100m, ReleaseStatus.Cancelled));

        // 400 (ativa) + 100 (cancelada romaneada); Pending fica de fora
        Assert.Equal(500m, pc.TotalShipmentReleasesWithoutProvisioning);
        Assert.Equal(500m, pc.TotalAvailableToReleaseWithoutProvisioning);

        // a variante com provisionamento inclui a Pending: 200 + 400 + 100
        Assert.Equal(700m, pc.TotalShipmentReleases);
        Assert.Equal(300m, pc.TotalAvailableToRelease);
    }

    [Fact]
    public void HasShipmentReleases_CancelledWithMovement_StillBlocks()
    {
        var pc = NewContract(1000m);
        pc.ShipmentReleases.Add(NewRelease(1000m, 300m, ReleaseStatus.Cancelled));

        Assert.True(pc.HasShipmentReleases);
    }

    [Fact]
    public void HasShipmentReleases_CancelledWithoutMovement_DoesNotBlock()
    {
        var pc = NewContract(1000m);
        pc.ShipmentReleases.Add(NewRelease(1000m, 0m, ReleaseStatus.Cancelled));

        Assert.False(pc.HasShipmentReleases);
    }
}
