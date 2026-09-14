using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// Romaneio de Perda/Sobra de armazém só nasce e morre pela Conferência de Saldo. As telas
/// genéricas de Romaneios (confirmar, cancelar, estornar) não podem mexer nele.
/// </summary>
public class WarehouseAdjustmentTransactionGuardsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<StorageTransaction> SeedAsync(
        StorageTransactionType type, StorageTransactionsStatus status)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "AJ-0001",
            CardCode = "ARM-T",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM-T",
            BranchCode = "01",
            TransactionType = type,
            TransactionStatus = status,
            TransactionOrigin = TransactionCode.WarehouseReconciliation,
            GrossWeight = 100m,
            NetWeight = 100m,
        };

        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }

    private async Task<StorageTransactionsStatus> StatusOf(Guid key) =>
        (await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key))
        .TransactionStatus;

    [Theory]
    [InlineData(StorageTransactionType.WarehouseLoss)]
    [InlineData(StorageTransactionType.WarehouseGain)]
    public async Task Generic_confirmation_refuses_warehouse_adjustments(StorageTransactionType type)
    {
        var transaction = await SeedAsync(type, StorageTransactionsStatus.Pending);
        var service = new StorageTransactionsConfirmedService(
            _db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_CONFIRMABLE", ex.Message);
        Assert.Equal(StorageTransactionsStatus.Pending, await StatusOf(transaction.Key));
    }

    [Fact]
    public async Task Generic_cancel_refuses_reconciliation_origin()
    {
        var transaction = await SeedAsync(
            StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Confirmed);
        var service = new StorageTransactionsCancelService(
            _db, new ShipmentReleasesRecalculateShippedService(_db.Context));

        await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction.Key, "tester"));

        Assert.Equal(StorageTransactionsStatus.Confirmed, await StatusOf(transaction.Key));
    }

    [Fact]
    public async Task Generic_reverse_refuses_reconciliation_origin()
    {
        var transaction = await SeedAsync(
            StorageTransactionType.WarehouseGain, StorageTransactionsStatus.Confirmed);
        // O saldo de lote não é consultado: o guard de origem dispara antes.
        var service = new StorageTransactionsReverseService(
            _db,
            new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

        await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction.Key, "tester"));

        Assert.Equal(StorageTransactionsStatus.Confirmed, await StatusOf(transaction.Key));
    }

    private StorageTransactionsCopyService CopyService() => new(
        _db,
        new FakeDocNumberSequenceService(),
        new StorageTransactionsCreateService(
            _db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { ["ARM-T"] = "Armazém Terceiro" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["ARM-T"] = "Armazém Terceiro" }),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance),
        new FakeStringLocalizer<Resource>());

    [Theory]
    [InlineData(StorageTransactionType.WarehouseLoss)]
    [InlineData(StorageTransactionType.WarehouseGain)]
    public async Task Copy_by_key_refuses_warehouse_adjustments(StorageTransactionType type)
    {
        var transaction = await SeedAsync(type, StorageTransactionsStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CopyService().ExecuteAsync(transaction.Key, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_COPYABLE", ex.Message);
        Assert.Equal(1, await _db.Context.StorageTransactions.CountAsync());
    }

    [Theory]
    [InlineData(StorageTransactionType.WarehouseLoss)]
    [InlineData(StorageTransactionType.WarehouseGain)]
    public async Task Copy_by_entity_refuses_warehouse_adjustments(StorageTransactionType type)
    {
        var transaction = await SeedAsync(type, StorageTransactionsStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CopyService().ExecuteAsync(transaction, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_COPYABLE", ex.Message);
        Assert.Equal(1, await _db.Context.StorageTransactions.CountAsync());
    }

    [Fact]
    public async Task Copy_still_works_for_ordinary_transaction_types()
    {
        var transaction = await SeedAsync(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed);

        var clone = await CopyService().ExecuteAsync(transaction.Key, "tester");

        Assert.NotEqual(transaction.Key, clone.Key);
        Assert.Equal(2, await _db.Context.StorageTransactions.CountAsync());
    }
}
