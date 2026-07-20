using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageEntryTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageEntryTransactions;

public class StorageEntryTransactionsCancelServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    /// <summary>
    /// O saldo real vem de SQL (Dapper). Aqui interessa a decisão do guard,
    /// não a tradução da query — por isso o valor é injetado.
    /// </summary>
    private sealed class FakeBalance(decimal balance) : IStorageAddressBalanceReader
    {
        public decimal GetBalance(string storageAddressCode) => balance;
    }

    private StorageEntryTransactionsCancelService CreateService(decimal lotBalance) => new(
        _db,
        new PurchaseContractsAllocationDeleteService(
            _db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
        new StorageTransactionsCancelService(
            _db, new ShipmentReleasesRecalculateShippedService(_db.Context)),
        new FakeBalance(lotBalance));

    private static StorageTransaction NewTx(StorageTransactionType type, decimal netWeight) => new()
    {
        Key = Guid.NewGuid(),
        Code = "ST",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TransactionType = type,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        TransactionOrigin = TransactionCode.StorageTransaction,
        NetWeight = netWeight,
        AvaiableVolumeToAllocate = 0m,
    };

    /// <summary>Monta o cenário completo: contrato alocado + par Purchase/Receipt + lote.</summary>
    private async Task<StorageEntryTransaction> SeedAsync(
        decimal netWeight = 1000m,
        ContractStatus contractStatus = ContractStatus.Approved,
        StorageTransactionsStatus receiptStatus = StorageTransactionsStatus.Confirmed)
    {
        var lot = new StorageAddress
        {
            Code = "LOTE-01",
            Description = "Lote próprio",
            CardCode = "C0001",
            ItemCode = "SOJA",
            WarehouseCode = "02",
            UoM = "KG",
            Status = StorageAddressStatus.Open,
        };

        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 5000m,
            AllocatedVolume = netWeight,
            Status = contractStatus,
        };

        var purchase = NewTx(StorageTransactionType.Purchase, netWeight);
        var receipt = NewTx(StorageTransactionType.Receipt, netWeight);
        receipt.StorageAddressCode = lot.Code;
        receipt.TransactionStatus = receiptStatus;

        var allocation = new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            StorageTransactionKey = purchase.Key,
            Volume = netWeight,
        };

        var entry = new StorageEntryTransaction
        {
            Key = Guid.NewGuid(),
            PurchaseStorageTransactionKey = purchase.Key,
            ReceiptStorageTransactionKey = receipt.Key,
            PurchaseContractKey = contract.Key,
            StorageAddressCode = lot.Code,
            Status = StorageEntryTransactionStatus.Confirmed,
            AllocatedVolume = netWeight,
            ReceiptNetWeight = netWeight,
        };

        _db.Context.StorageAddresses.Add(lot);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.StorageTransactions.AddRange(purchase, receipt);
        _db.Context.PurchaseContractsAllocations.Add(allocation);
        _db.Context.StorageEntryTransactions.Add(entry);
        await _db.Context.SaveChangesAsync();

        return entry;
    }

    [Fact]
    public async Task Cancel_MarksEntryAsCancelled()
    {
        var entry = await SeedAsync();

        await CreateService(lotBalance: 1000m).ExecuteAsync(entry.Key, "tester");

        var reloaded = await _db.Context.StorageEntryTransactions
            .AsNoTracking().SingleAsync(x => x.Key == entry.Key);
        Assert.Equal(StorageEntryTransactionStatus.Cancelled, reloaded.Status);
        Assert.Equal("tester", reloaded.CanceledBy);
    }

    [Fact]
    public async Task Cancel_ReturnsVolumeToPurchaseContract()
    {
        var entry = await SeedAsync(netWeight: 1000m);

        await CreateService(lotBalance: 1000m).ExecuteAsync(entry.Key, "tester");

        Assert.Empty(await _db.Context.PurchaseContractsAllocations.AsNoTracking().ToListAsync());

        var contract = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == entry.PurchaseContractKey);
        Assert.Equal(0m, contract.AllocatedVolume);
    }

    [Fact]
    public async Task Cancel_CancelsBothStorageTransactions()
    {
        var entry = await SeedAsync();

        await CreateService(lotBalance: 1000m).ExecuteAsync(entry.Key, "tester");

        var purchase = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == entry.PurchaseStorageTransactionKey);
        var receipt = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == entry.ReceiptStorageTransactionKey);

        Assert.Equal(StorageTransactionsStatus.Cancelled, purchase.TransactionStatus);
        Assert.Equal(StorageTransactionsStatus.Cancelled, receipt.TransactionStatus);
    }

    [Fact]
    public async Task Cancel_BlocksWhenLotBalanceIsInsufficient()
    {
        var entry = await SeedAsync(netWeight: 1000m);

        // O produto já saiu do lote — desfazer a entrada deixaria saldo negativo.
        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService(lotBalance: 400m).ExecuteAsync(entry.Key, "tester"));

        Assert.Contains("saldo", ex.Message, StringComparison.OrdinalIgnoreCase);

        var reloaded = await _db.Context.StorageEntryTransactions
            .AsNoTracking().SingleAsync(x => x.Key == entry.Key);
        Assert.Equal(StorageEntryTransactionStatus.Confirmed, reloaded.Status);
    }

    [Fact]
    public async Task Cancel_BlocksWhenAnyTransactionIsInvoiced()
    {
        var entry = await SeedAsync(receiptStatus: StorageTransactionsStatus.Invoiced);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService(lotBalance: 1000m).ExecuteAsync(entry.Key, "tester"));

        Assert.Contains("faturado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_BlocksWhenContractIsFinished()
    {
        var entry = await SeedAsync(contractStatus: ContractStatus.Finished);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService(lotBalance: 1000m).ExecuteAsync(entry.Key, "tester"));

        Assert.Contains("encerrado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_BlocksWhenAlreadyCancelled()
    {
        var entry = await SeedAsync();
        var service = CreateService(lotBalance: 1000m);
        await service.ExecuteAsync(entry.Key, "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(entry.Key, "tester"));

        Assert.Contains("cancelad", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
