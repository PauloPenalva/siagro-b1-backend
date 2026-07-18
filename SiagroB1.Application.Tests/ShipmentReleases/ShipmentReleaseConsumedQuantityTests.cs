using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// ConsumedQuantity é o volume que a liberação consome do contrato de origem.
/// Cancelada => apenas o efetivamente romaneado; caso contrário, o total liberado.
/// </summary>
public class ShipmentReleaseConsumedQuantityTests
{
    private static ShipmentRelease New(decimal released, decimal shipped, ReleaseStatus status) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = Guid.NewGuid(),
        DeliveryLocationCode = "01",
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
    };

    [Theory]
    [InlineData(ReleaseStatus.Pending)]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Completed)]
    public void ConsumedQuantity_NotCancelled_IsReleasedQuantity(ReleaseStatus status)
    {
        var sr = New(released: 1000m, shipped: 300m, status: status);
        Assert.Equal(1000m, sr.ConsumedQuantity);
    }

    [Fact]
    public void ConsumedQuantity_CancelledWithMovement_IsShippedQuantity()
    {
        var sr = New(released: 1000m, shipped: 300m, status: ReleaseStatus.Cancelled);
        Assert.Equal(300m, sr.ConsumedQuantity);
    }

    [Fact]
    public void ConsumedQuantity_CancelledWithoutMovement_IsZero()
    {
        var sr = New(released: 1000m, shipped: 0m, status: ReleaseStatus.Cancelled);
        Assert.Equal(0m, sr.ConsumedQuantity);
    }

    [Fact]
    public void ConsumedQuantity_CancelledWithNegativeShipped_ClampsToZero()
    {
        // mais devoluções que embarques não pode devolver volume extra ao contrato
        var sr = New(released: 1000m, shipped: -50m, status: ReleaseStatus.Cancelled);
        Assert.Equal(0m, sr.ConsumedQuantity);
    }

    // ---------- volume devolvido ao contrato pelo cancelamento ----------

    [Fact]
    public void ReturnedToContract_CancelledWithMovement_IsUnshippedBalance()
    {
        var sr = New(released: 1000m, shipped: 300m, status: ReleaseStatus.Cancelled);
        Assert.Equal(700m, sr.ReturnedToContractQuantity);
    }

    [Fact]
    public void ReturnedToContract_CancelledWithoutMovement_IsFullReleasedQuantity()
    {
        var sr = New(released: 1000m, shipped: 0m, status: ReleaseStatus.Cancelled);
        Assert.Equal(1000m, sr.ReturnedToContractQuantity);
    }

    [Theory]
    [InlineData(ReleaseStatus.Pending)]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Completed)]
    public void ReturnedToContract_NotCancelled_IsZero(ReleaseStatus status)
    {
        // nada voltou: a liberação ainda consome o total liberado
        var sr = New(released: 1000m, shipped: 300m, status: status);
        Assert.Equal(0m, sr.ReturnedToContractQuantity);
    }

    [Fact]
    public void ReturnedToContract_OverShipped_ClampsToZero()
    {
        var sr = New(released: 1000m, shipped: 1200m, status: ReleaseStatus.Cancelled);
        Assert.Equal(0m, sr.ReturnedToContractQuantity);
    }

    [Fact]
    public void ReturnedToContract_PlusConsumed_EqualsReleased()
    {
        var sr = New(released: 1000m, shipped: 300m, status: ReleaseStatus.Cancelled);
        Assert.Equal(sr.ReleasedQuantity, sr.ConsumedQuantity + sr.ReturnedToContractQuantity);
    }

    [Fact]
    public void CancellationReason_DefaultsToNull()
    {
        var sr = New(released: 1000m, shipped: 0m, status: ReleaseStatus.Actived);
        Assert.Null(sr.CancellationReason);
    }
}
