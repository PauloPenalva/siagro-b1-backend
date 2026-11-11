using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Quinto termo do saldo da carga (GAC-1213): o volume perdido em quebra de trajeto, e o efeito
/// dele no recálculo da carga.
/// </summary>
public class ShipmentLoadsRecalculateTransitLossServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoad NewLoad(string code = "CG000010")
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = code,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            BranchCode = "01",
            TotalQuantity = 40_000m,
            Status = ShipmentLoadStatus.Open,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction NewTransaction(
        StorageTransactionType type,
        StorageTransactionsStatus status,
        decimal grossWeight,
        Guid? refusedFromLoadKey = null,
        Guid? shipmentLoadKey = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = $"RM{Guid.NewGuid():N}"[..10],
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = type,
            TransactionStatus = status,
            RefusedFromShipmentLoadKey = refusedFromLoadKey,
            ShipmentLoadKey = shipmentLoadKey,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    private Task<decimal> LossAsync(ShipmentLoad load) =>
        ShipmentLoadsRecalculateTransitLossService.CalculateTransitLossAsync(_db.Context, load.Key);

    [Fact]
    public async Task A_load_with_no_loss_has_nothing_lost()
    {
        var load = NewLoad();
        await _db.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await LossAsync(load));
    }

    [Fact]
    public async Task Transit_loss_transactions_are_summed_by_gross_weight()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.TransitLoss,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: load.Key);
        NewTransaction(StorageTransactionType.TransitLoss,
            StorageTransactionsStatus.Confirmed, 10_000.0004m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(25_000m, await LossAsync(load));
    }

    [Fact]
    public async Task A_cancelled_transit_loss_does_not_count()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.TransitLoss,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: load.Key);
        NewTransaction(StorageTransactionType.TransitLoss,
            StorageTransactionsStatus.Cancelled, 9_000m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(15_000m, await LossAsync(load));
    }

    /// <summary>A devolução em armazém (tipo 12) é outro termo do saldo e não pode ser contada duas vezes.</summary>
    [Fact]
    public async Task A_warehouse_return_does_not_count_as_loss()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.SalesShipmentReturn,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await LossAsync(load));
    }

    [Fact]
    public async Task A_loss_of_another_load_does_not_count()
    {
        var load = NewLoad();
        var other = NewLoad("CG000011");
        NewTransaction(StorageTransactionType.TransitLoss,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: other.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await LossAsync(load));
    }

    [Fact]
    public async Task Recalculation_writes_the_loss_and_the_status_and_is_stable_on_a_second_run()
    {
        var load = NewLoad();
        var shipment = NewTransaction(StorageTransactionType.SalesShipment,
            StorageTransactionsStatus.Confirmed, 40_000m, shipmentLoadKey: load.Key);
        NewTransaction(StorageTransactionType.TransitLoss,
            StorageTransactionsStatus.Confirmed, 40_000m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, null);
        await _db.SaveChangesAsync();

        Assert.Equal(40_000m, load.TransitLossQuantity);
        Assert.Equal(ShipmentLoadStatus.TransitLoss, load.Status);
        Assert.Equal(StorageTransactionsStatus.Invoiced, shipment.TransactionStatus);

        // Recalcular de novo mantém a situação e a projeção nos romaneios de embarque.
        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, null);
        await _db.SaveChangesAsync();

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(ShipmentLoadStatus.TransitLoss, saved.Status);
        Assert.Equal(StorageTransactionsStatus.Invoiced,
            (await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipment.Key)).TransactionStatus);
    }

    [Fact]
    public void ResolveClosure_keeps_the_transit_loss_status()
    {
        Assert.Equal(
            ShipmentLoadStatus.TransitLoss,
            ShipmentLoadsRecalculateInvoicedService.ResolveClosure(ShipmentLoadStatus.TransitLoss, true, true));
    }

    [Fact]
    public void The_change_log_describes_the_status_in_portuguese()
    {
        Assert.Equal("Quebra de trajeto", ShipmentLoadChangeLogFields.DescribeStatus(ShipmentLoadStatus.TransitLoss));
    }

    [Fact]
    public async Task The_composition_guard_refuses_a_load_with_transit_loss()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.TransitLoss,
            StorageTransactionsStatus.Confirmed, 12_500m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => new ShipmentLoadsCompositionGuardService(_db.Context).EnsureCanChangeCompositionAsync(load));

        Assert.Equal(
            $"A carga CG000010 teve {12_500m:N3} perdido(s) em quebra de trajeto e sua composição não pode mais ser alterada.",
            error.Message);
    }
}
