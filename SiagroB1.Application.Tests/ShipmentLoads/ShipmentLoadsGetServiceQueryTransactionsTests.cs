using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Grid da carga (GAC-1177 v2, Task 4): depois de uma troca de liberação, a Expedição original
/// some de <see cref="StorageTransaction.ShipmentLoadKey"/> e passa a se ligar à carga só pelo
/// documento <see cref="ShippingReleaseChange"/>. <see cref="ShipmentLoadsGetService.QueryTransactions"/>
/// precisa enxergar a vigente, a substituída e o estorno (12) — mas não a perna de compra (8/9),
/// que não aparece nesta tela, nem romaneio de outra carga.
/// </summary>
public class ShipmentLoadsGetServiceQueryTransactionsTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly Guid _loadKey = Guid.NewGuid();

    private ShipmentLoadsGetService Service() =>
        new(_db, NullLogger<ShipmentLoadsGetService>.Instance);

    private StorageTransaction Transaction(
        string code,
        StorageTransactionType type,
        Guid? shipmentLoadKey,
        Guid? replacedByShippingReleaseChangeKey = null,
        Guid? shippingReleaseChangeKey = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            GrossWeight = 1000m,
            NetWeight = 1000m,
            TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = shipmentLoadKey,
            ReplacedByShippingReleaseChangeKey = replacedByShippingReleaseChangeKey,
            ShippingReleaseChangeKey = shippingReleaseChangeKey,
        };
        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    private ShippingReleaseChange Change(Guid key, Guid shipmentLoadKey)
    {
        var change = new ShippingReleaseChange
        {
            Key = key,
            ShipmentLoadKey = shipmentLoadKey,
            OperationGroupKey = Guid.NewGuid(),
            OriginalSalesStorageTransactionKey = Guid.NewGuid(),
            ReturnSalesStorageTransactionKey = Guid.NewGuid(),
            NewSalesStorageTransactionKey = Guid.NewGuid(),
            OriginalQuantity = 1000m,
            NewQuantity = 1000m,
            Reason = "teste",
        };
        _db.Context.ShippingReleaseChanges.Add(change);
        return change;
    }

    [Fact]
    public async Task Returns_current_shipment_still_linked_to_the_load()
    {
        var current = Transaction("R-CURRENT", StorageTransactionType.SalesShipment, _loadKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.Contains(result, x => x.Key == current.Key);
    }

    [Fact]
    public async Task Returns_the_original_replaced_by_a_release_change_even_without_the_load_key()
    {
        var changeKey = Guid.NewGuid();
        Change(changeKey, _loadKey);
        var replaced = Transaction(
            "R-REPLACED", StorageTransactionType.SalesShipment,
            shipmentLoadKey: null, replacedByShippingReleaseChangeKey: changeKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.Contains(result, x => x.Key == replaced.Key);
    }

    [Fact]
    public async Task Returns_the_type_12_reversal_generated_by_the_change()
    {
        var changeKey = Guid.NewGuid();
        Change(changeKey, _loadKey);
        var reversal = Transaction(
            "R-REVERSAL", StorageTransactionType.SalesShipmentReturn,
            shipmentLoadKey: null, shippingReleaseChangeKey: changeKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.Contains(result, x => x.Key == reversal.Key);
    }

    [Fact]
    public async Task Does_not_return_the_purchase_leg_8_or_9_of_the_change()
    {
        var changeKey = Guid.NewGuid();
        Change(changeKey, _loadKey);
        var replacedPurchase = Transaction(
            "R-REPLACED-8", StorageTransactionType.Purchase,
            shipmentLoadKey: null, replacedByShippingReleaseChangeKey: changeKey);
        var reversalPurchase = Transaction(
            "R-REVERSAL-9", StorageTransactionType.PurchaseReturn,
            shipmentLoadKey: null, shippingReleaseChangeKey: changeKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.DoesNotContain(result, x => x.Key == replacedPurchase.Key);
        Assert.DoesNotContain(result, x => x.Key == reversalPurchase.Key);
    }

    [Fact]
    public async Task Does_not_return_a_shipment_from_another_load()
    {
        var otherLoadKey = Guid.NewGuid();
        var other = Transaction("R-OTHER", StorageTransactionType.SalesShipment, otherLoadKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.DoesNotContain(result, x => x.Key == other.Key);
    }

    /// <summary>
    /// A Expedição nova (7) criada pela troca solta (<c>ShipmentLoadKey</c> volta a
    /// <c>null</c>) quando a carga de destino é cancelada ou o romaneio é desvinculado — mas
    /// continua com <c>ShippingReleaseChangeKey</c> apontando para a troca. O branch que enxerga
    /// pelo <c>ShippingReleaseChangeKey</c> existe só para trazer o estorno (12); se ele também
    /// casar tipo 7, a Expedição solta reaparece no grid da carga ORIGINAL mesmo já estando
    /// livre para ser vinculada a outra carga (ver Attach.controller.ts).
    /// </summary>
    [Fact]
    public async Task Does_not_return_a_new_shipment_detached_from_the_load_even_with_the_change_key()
    {
        var changeKey = Guid.NewGuid();
        Change(changeKey, _loadKey);
        var detachedNew = Transaction(
            "R-NEW-DETACHED", StorageTransactionType.SalesShipment,
            shipmentLoadKey: null, shippingReleaseChangeKey: changeKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.DoesNotContain(result, x => x.Key == detachedNew.Key);
    }

    [Fact]
    public async Task Returns_the_new_shipment_while_still_attached_to_the_load_via_shipment_load_key()
    {
        var changeKey = Guid.NewGuid();
        Change(changeKey, _loadKey);
        var attachedNew = Transaction(
            "R-NEW-ATTACHED", StorageTransactionType.SalesShipment,
            shipmentLoadKey: _loadKey, shippingReleaseChangeKey: changeKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.Contains(result, x => x.Key == attachedNew.Key);
    }

    [Fact]
    public async Task Grid_shows_current_replaced_and_reversal_together_for_the_full_change()
    {
        var changeKey = Guid.NewGuid();
        Change(changeKey, _loadKey);
        var replaced = Transaction(
            "R-REPLACED", StorageTransactionType.SalesShipment,
            shipmentLoadKey: null, replacedByShippingReleaseChangeKey: changeKey);
        var reversal = Transaction(
            "R-REVERSAL", StorageTransactionType.SalesShipmentReturn,
            shipmentLoadKey: null, shippingReleaseChangeKey: changeKey);
        var current = Transaction(
            "R-NEW", StorageTransactionType.SalesShipment,
            shipmentLoadKey: _loadKey, shippingReleaseChangeKey: changeKey);
        await _db.Context.SaveChangesAsync();

        var result = await Service().QueryTransactions(_loadKey).ToListAsync();

        Assert.Equal(3, result.Count);
        Assert.Contains(result, x => x.Key == replaced.Key);
        Assert.Contains(result, x => x.Key == reversal.Key);
        Assert.Contains(result, x => x.Key == current.Key);
    }
}
