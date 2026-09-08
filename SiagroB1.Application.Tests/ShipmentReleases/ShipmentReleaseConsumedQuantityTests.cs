using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// ConsumedQuantity é o volume que a liberação consome do contrato de origem.
/// Cancelada => apenas o efetivamente romaneado; caso contrário, o total liberado.
/// </summary>
public class ShipmentReleaseConsumedQuantityTests
{
    private static ShipmentRelease New(
        decimal released,
        decimal shipped,
        ReleaseStatus status,
        ReleaseOrigin origin = ReleaseOrigin.Standard) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = Guid.NewGuid(),
        DeliveryLocationCode = "01",
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
        Origin = origin,
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

    // ---------- liberação nascida de devolução ao armazém ----------
    // O volume já foi debitado do contrato quando a mercadoria saiu pela primeira
    // vez. Esta liberação é só a porta de saída do grão que voltou, então não pode
    // consumir o contrato de novo nem devolver volume a ele.

    [Theory]
    [InlineData(ReleaseStatus.Pending)]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    public void ConsumedQuantity_SalesReturnOrigin_IsAlwaysZero(ReleaseStatus status)
    {
        var sr = New(released: 1000m, shipped: 300m, status: status, origin: ReleaseOrigin.SalesReturn);
        Assert.Equal(0m, sr.ConsumedQuantity);
    }

    [Theory]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Cancelled)]
    public void ReturnedToContract_SalesReturnOrigin_IsAlwaysZero(ReleaseStatus status)
    {
        // Cancelar uma liberação de devolução não pode CREDITAR o contrato: ela
        // nunca o debitou. Sem esta regra o cancelamento devolveria 1.000 fantasmas.
        var sr = New(released: 1000m, shipped: 300m, status: status, origin: ReleaseOrigin.SalesReturn);
        Assert.Equal(0m, sr.ReturnedToContractQuantity);
    }

    [Fact]
    public void AvailableQuantity_SalesReturnOrigin_StillFollowsShipped()
    {
        // O saldo a embarcar continua valendo: é ele que põe a liberação na
        // Expedição de Grãos. Só o consumo do CONTRATO é que foi zerado.
        var sr = New(released: 1000m, shipped: 300m, status: ReleaseStatus.Actived, origin: ReleaseOrigin.SalesReturn);
        Assert.Equal(700m, sr.AvailableQuantity);
    }

    [Fact]
    public void CancellationReason_DefaultsToNull()
    {
        var sr = New(released: 1000m, shipped: 0m, status: ReleaseStatus.Actived);
        Assert.Null(sr.CancellationReason);
    }
}
