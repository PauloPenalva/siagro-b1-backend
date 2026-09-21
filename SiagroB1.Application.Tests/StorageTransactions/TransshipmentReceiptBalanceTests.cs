using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// O romaneio 15 (<see cref="StorageTransactionType.TransshipmentReceipt"/>, GAC-1181): entrada
/// do transbordo no armazém intermediário. Credita o saldo do armazém como a devolução ao
/// armazém (12) — sem lote, sem contrato — e a liberação que ele origina não consome o contrato
/// de compra.
/// </summary>
public class TransshipmentReceiptBalanceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsConfirmedService Service() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private static StorageTransaction Entry(decimal grossWeight, StorageTransactionsStatus status) => new()
    {
        TransactionType = StorageTransactionType.TransshipmentReceipt,
        TransactionStatus = status,
        TransactionDate = DateTime.Today,
        BranchCode = "01",
        CardCode = "F001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "ARM99",
        GrossWeight = grossWeight,
        NetWeight = grossWeight,
    };

    [Fact]
    public async Task ConfirmedTransshipmentReceipt_CreditsTheWarehouse()
    {
        _db.Context.StorageTransactions.Add(Entry(29_800, StorageTransactionsStatus.Confirmed));
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, "ARM99", "SOJA");

        Assert.Equal(29_800m, balance);
    }

    [Fact]
    public async Task CancelledTransshipmentReceipt_DoesNotCredit()
    {
        _db.Context.StorageTransactions.Add(Entry(29_800, StorageTransactionsStatus.Cancelled));
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, "ARM99", "SOJA");

        Assert.Equal(decimal.Zero, balance);
    }

    /// <summary>
    /// O ramo de confirmação do 15 em si: pendente vira Confirmed e o líquido nasce igual ao
    /// bruto, sem descontos — não há tabela de custos no transbordo.
    /// </summary>
    [Fact]
    public async Task PendingTransshipmentReceipt_ConfirmsAtGrossWeight()
    {
        var transaction = Entry(29_800, StorageTransactionsStatus.Pending);
        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(transaction, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, transaction.TransactionStatus);
        Assert.Equal(29_800m, transaction.NetWeight);
        Assert.Equal(decimal.Zero, transaction.AvaiableVolumeToAllocate);
    }

    [Fact]
    public void TransshipmentRelease_DoesNotConsumeThePurchaseContract()
    {
        Assert.False(ReleaseOriginRules.ConsumesPurchaseContract(ReleaseOrigin.Transshipment));
        Assert.True(ReleaseOriginRules.ShipsWithoutPurchaseLeg(ReleaseOrigin.Transshipment));
        Assert.False(ReleaseOriginRules.RequiresStorageAddress(ReleaseOrigin.Transshipment));
    }
}
