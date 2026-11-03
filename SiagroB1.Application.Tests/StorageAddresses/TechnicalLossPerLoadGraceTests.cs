using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageAddresses;

/// <summary>
/// Quebra técnica carga a carga (lote 00000044 da HOM Yokotobi, planilha do João Tedesco):
/// cada entrada só entra na base depois da SUA carência, não da carência da primeira carga.
/// A quebra diária é lançada em kg inteiro (Round Half Up), como os demais pesos do lote.
/// </summary>
public class TechnicalLossPerLoadGraceTests
{
    private const string LotCode = "L044";

    private static StorageTransaction Receipt(DateTime date, decimal netWeight) => new()
    {
        Key = Guid.NewGuid(),
        StorageAddressCode = LotCode,
        TransactionType = StorageTransactionType.Receipt,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        TransactionDate = date,
        NetWeight = netWeight,
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
    };

    private static async Task<IUnitOfWork> SeedLot44()
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.ProcessingCosts.Add(new ProcessingCost
        {
            Code = "00005",
            Description = "Custo de teste",
            TechnicalLossRate = 0.0001m,
            TechnicalLossGraceDays = 31,
            TechnicalLossIntervalDays = 1,
            StoragePriceCalculationMethod = StoragePriceCalculationMethod.AfterFirstReceipting,
        });
        db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = LotCode,
            Description = "Lote 44",
            CardCode = "C0001",
            ItemCode = "SOJA",
            WarehouseCode = "01",
            UoM = "KG",
            Status = StorageAddressStatus.Open,
            Nature = StorageAddressNature.Regular,
            ProcessingCostCode = "00005",
        });
        db.Context.StorageTransactions.AddRange(
            Receipt(new DateTime(2026, 3, 11), 19007m),
            Receipt(new DateTime(2026, 3, 15), 19185m),
            Receipt(new DateTime(2026, 3, 16), 7566m));

        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return db;
    }

    private static async Task<Dictionary<DateTime, decimal>> RunUntilEndOfApril(IUnitOfWork db)
    {
        var orchestrator = new StorageAddressesCalculationOrchestratorService(
            new StorageAddressesDailyBalanceBuilderService(db),
            new StorageAddressesStorageChargeCalculatorService(db),
            new StorageAddressesTechnicalLossCalculatorService(db, new FakeDocNumberSequenceService()));

        for (var day = new DateTime(2026, 3, 11); day <= new DateTime(2026, 4, 30); day = day.AddDays(1))
            await orchestrator.ExecuteAsync(day);

        return await db.Context.StorageTransactions
            .Where(x => x.TransactionType == StorageTransactionType.TechnicalLoss)
            .ToDictionaryAsync(x => x.TransactionDate!.Value, x => x.NetWeight);
    }

    [Fact]
    public async Task EachLoadEntersTheBaseOnlyAfterItsOwnGracePeriod()
    {
        var losses = await RunUntilEndOfApril(await SeedLot44());

        Assert.False(losses.ContainsKey(new DateTime(2026, 4, 10)));
        // 11/04 a 14/04: só a 1ª carga (19.007 kg) venceu a carência.
        Assert.Equal(2m, losses[new DateTime(2026, 4, 11)]);
        Assert.Equal(2m, losses[new DateTime(2026, 4, 14)]);
        // 15/04: entra a 2ª carga (19.003 + 19.185 − 2... = 38.184 kg → 3,82 → 4).
        Assert.Equal(4m, losses[new DateTime(2026, 4, 15)]);
        // 16/04: entra a 3ª carga (45.746 kg → 4,57 → 5).
        Assert.Equal(5m, losses[new DateTime(2026, 4, 16)]);
        Assert.Equal(5m, losses[new DateTime(2026, 4, 30)]);
    }

    [Fact]
    public async Task DailyLossIsLaunchedInWholeKilograms()
    {
        var losses = await RunUntilEndOfApril(await SeedLot44());

        Assert.All(losses.Values, v => Assert.Equal(Math.Truncate(v), v));
        Assert.Equal(87m, losses.Values.Sum());
    }
}
