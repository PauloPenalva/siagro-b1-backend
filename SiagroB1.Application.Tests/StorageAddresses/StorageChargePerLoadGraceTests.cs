using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageAddresses;

/// <summary>
/// GAC-18: a armazenagem segue o mesmo esquema da quebra técnica — cada entrada só entra
/// na base de cobrança depois da SUA carência, não da carência da primeira carga.
/// </summary>
public class StorageChargePerLoadGraceTests
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

    private static async Task<Dictionary<DateTime, decimal>> RunUntilEndOfApril()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ProcessingCosts.Add(new ProcessingCost
        {
            Code = "00005",
            Description = "Custo de teste",
            StoragePrice = 0.01m,
            StorageGraceDays = 31,
            StorageBillingIntervalDays = 1,
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

        var orchestrator = new StorageAddressesCalculationOrchestratorService(
            new StorageAddressesDailyBalanceBuilderService(db),
            new StorageAddressesStorageChargeCalculatorService(db),
            new StorageAddressesTechnicalLossCalculatorService(db, new FakeDocNumberSequenceService()));

        for (var day = new DateTime(2026, 3, 11); day <= new DateTime(2026, 4, 30); day = day.AddDays(1))
            await orchestrator.ExecuteAsync(day);

        return await db.Context.StorageCharges
            .Where(x => x.ChargeType == StorageChargeType.Storage)
            .ToDictionaryAsync(x => x.PeriodEnd, x => x.TotalAmount);
    }

    [Fact]
    public async Task EachLoadEntersTheStorageBaseOnlyAfterItsOwnGracePeriod()
    {
        var charges = await RunUntilEndOfApril();

        Assert.False(charges.ContainsKey(new DateTime(2026, 4, 10)));
        // 11/04 a 14/04: só a 1ª carga (19.007 kg) venceu a carência.
        Assert.Equal(190.07m, charges[new DateTime(2026, 4, 11)]);
        Assert.Equal(190.07m, charges[new DateTime(2026, 4, 14)]);
        // 15/04: entra a 2ª carga (38.192 kg).
        Assert.Equal(381.92m, charges[new DateTime(2026, 4, 15)]);
        // 16/04: entra a 3ª carga (45.758 kg).
        Assert.Equal(457.58m, charges[new DateTime(2026, 4, 16)]);
    }
}
