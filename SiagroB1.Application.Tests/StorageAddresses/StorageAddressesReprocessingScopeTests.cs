using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageAddresses;

/// <summary>
/// GAC-18: "Reprocessar Saldo por Lote" apagava só o lote escolhido, mas recalculava TODOS os lotes
/// em cada dia do intervalo. Na homologação, com a tabela de custos trocada de 30 para 1 dia, os
/// outros lotes ganharam quebras diárias por cima das quebras de 30 dias que já tinham.
/// </summary>
public class StorageAddressesReprocessingScopeTests
{
    private const string Target = "L100";
    private const string Other = "L300";
    private static readonly DateTime Day1 = new(2026, 9, 1);

    private static StorageAddress NewAddress(string code) => new()
    {
        Code = code,
        Description = "Lote " + code,
        CardCode = "C0001",
        ItemCode = "SOJA",
        WarehouseCode = "01",
        UoM = "KG",
        Status = StorageAddressStatus.Open,
        Nature = StorageAddressNature.Regular,
        ProcessingCostCode = "C1",
    };

    private static StorageTransaction Receipt(string lot) => new()
    {
        Key = Guid.NewGuid(),
        StorageAddressCode = lot,
        TransactionType = StorageTransactionType.Receipt,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        TransactionDate = Day1,
        NetWeight = 10_000m,
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
    };

    private static async Task<IUnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ProcessingCosts.Add(new ProcessingCost
        {
            Code = "C1",
            Description = "Custo de teste",
            StoragePrice = 5m,
            StorageGraceDays = 0,
            StorageBillingIntervalDays = 1,
            TechnicalLossRate = 0.0001m,
            TechnicalLossGraceDays = 0,
            TechnicalLossIntervalDays = 1,
            StoragePriceCalculationMethod = StoragePriceCalculationMethod.AfterFirstReceipting,
        });
        db.Context.StorageAddresses.AddRange(NewAddress(Target), NewAddress(Other));
        db.Context.StorageTransactions.AddRange(Receipt(Target), Receipt(Other));
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return db;
    }

    private static StorageAddressesReprocessingService Service(IUnitOfWork db) => new(db,
        new StorageAddressesCalculationOrchestratorService(
            new StorageAddressesDailyBalanceBuilderService(db),
            new StorageAddressesStorageChargeCalculatorService(db),
            new StorageAddressesTechnicalLossCalculatorService(db, new FakeDocNumberSequenceService())));

    private static Task<int> LossesAsync(IUnitOfWork db, string lot) =>
        db.Context.StorageTransactions.CountAsync(x =>
            x.StorageAddressCode == lot && x.TransactionType == StorageTransactionType.TechnicalLoss);

    [Fact]
    public async Task Reprocessing_a_lot_does_not_calculate_the_other_lots()
    {
        var db = await SeedAsync();

        await Service(db).ReprocessAsync(Target, Day1, Day1.AddDays(2));

        Assert.Equal(3, await LossesAsync(db, Target));
        Assert.Equal(0, await LossesAsync(db, Other));
        Assert.False(await db.Context.StorageCharges.AnyAsync(x => x.StorageAddressCode == Other));
        Assert.False(await db.Context.StorageDailyBalances.AnyAsync(x => x.StorageAddressCode == Other));
    }

    [Fact]
    public async Task Reprocessing_a_lot_twice_does_not_duplicate_its_losses()
    {
        var db = await SeedAsync();

        await Service(db).ReprocessAsync(Target, Day1, Day1.AddDays(2));
        db.Context.ChangeTracker.Clear();
        await Service(db).ReprocessAsync(Target, Day1, Day1.AddDays(2));

        Assert.Equal(3, await LossesAsync(db, Target));
    }

    [Fact]
    public async Task Daily_job_still_calculates_every_lot()
    {
        var db = await SeedAsync();
        var orchestrator = new StorageAddressesCalculationOrchestratorService(
            new StorageAddressesDailyBalanceBuilderService(db),
            new StorageAddressesStorageChargeCalculatorService(db),
            new StorageAddressesTechnicalLossCalculatorService(db, new FakeDocNumberSequenceService()));

        await orchestrator.ExecuteAsync(Day1);

        Assert.Equal(1, await LossesAsync(db, Target));
        Assert.Equal(1, await LossesAsync(db, Other));
    }
}
