using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageAddresses;

/// <summary>
/// Encerramento do lote de armazenagem: o usuário encerra quando quiser, informando a data de
/// encerramento. Essa data é o ÚLTIMO dia considerado — saldo diário, armazenagem e quebra técnica
/// são calculados até ela (inclusive) e nunca depois, nem no reprocessamento.
/// </summary>
public class StorageAddressClosingTests
{
    private const string Lot = "L100";
    private static readonly DateTime Day = new(2026, 9, 1);

    private static StorageAddress NewAddress(
        StorageAddressStatus status = StorageAddressStatus.Open, DateTime? closingDate = null) => new()
    {
        Code = Lot,
        Description = "Lote",
        CardCode = "C0001",
        ItemCode = "SOJA",
        WarehouseCode = "01",
        UoM = "KG",
        Status = status,
        ClosingDate = closingDate,
        Nature = StorageAddressNature.Regular,
        ProcessingCostCode = "PC01",
        TransactionOrigin = TransactionCode.StorageAddress,
    };

    private static StorageTransaction Movement(
        DateTime date,
        StorageTransactionType type = StorageTransactionType.Receipt,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        decimal netWeight = 100m,
        string? comments = null) => new()
    {
        Key = Guid.NewGuid(),
        StorageAddressCode = Lot,
        TransactionType = type,
        TransactionStatus = status,
        TransactionDate = date,
        NetWeight = netWeight,
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        Comments = comments,
    };

    private static StorageDailyBalance Balance(DateTime date) => new()
    {
        StorageAddressCode = Lot,
        BalanceDate = date,
        ClosingBalance = 100m,
        BillableBalance = 100m,
    };

    private static StorageCharge Charge(DateTime periodEnd, bool invoiced = false) => new()
    {
        StorageAddressCode = Lot,
        ChargeType = StorageChargeType.Storage,
        PeriodStart = periodEnd,
        PeriodEnd = periodEnd,
        IsInvoiced = invoiced,
    };

    private static async Task<IUnitOfWork> Seed(StorageAddress address, params object[] rows)
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.ProcessingCosts.Add(new ProcessingCost
        {
            Code = "PC01",
            Description = "Custo",
            StoragePrice = 5m,
            StorageGraceDays = 0,
            StorageBillingIntervalDays = 1,
            TechnicalLossRate = 1m,
            TechnicalLossGraceDays = 0,
            TechnicalLossIntervalDays = 1,
            StoragePriceCalculationMethod = StoragePriceCalculationMethod.AfterFirstReceipting,
        });
        db.Context.StorageAddresses.Add(address);
        db.Context.AddRange(rows);

        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return db;
    }

    // ---- Cálculos diários ----

    [Fact]
    public async Task StorageCharge_StillChargesAClosedLotUpToItsClosingDate()
    {
        var db = await Seed(NewAddress(StorageAddressStatus.Closed, Day), Movement(Day), Balance(Day));

        await new StorageAddressesStorageChargeCalculatorService(db).CalculateAsync(Day);

        Assert.True(await db.Context.StorageCharges.AnyAsync(x => x.StorageAddressCode == Lot));
    }

    [Fact]
    public async Task StorageCharge_SkipsAClosedLotAfterItsClosingDate()
    {
        var next = Day.AddDays(1);
        var db = await Seed(NewAddress(StorageAddressStatus.Closed, Day), Movement(Day), Balance(Day), Balance(next));

        await new StorageAddressesStorageChargeCalculatorService(db).CalculateAsync(next);

        Assert.False(await db.Context.StorageCharges.AnyAsync(x => x.StorageAddressCode == Lot));
    }

    [Fact]
    public async Task TechnicalLoss_SkipsAClosedLotAfterItsClosingDate()
    {
        var next = Day.AddDays(1);
        var db = await Seed(NewAddress(StorageAddressStatus.Closed, Day), Movement(Day), Balance(Day), Balance(next));

        await new StorageAddressesTechnicalLossCalculatorService(db, new FakeDocNumberSequenceService())
            .CalculateAsync(next);

        Assert.False(await db.Context.StorageCharges.AnyAsync(x => x.StorageAddressCode == Lot));
        Assert.False(await db.Context.StorageTransactions
            .AnyAsync(x => x.TransactionType == StorageTransactionType.TechnicalLoss));
    }

    [Fact]
    public async Task DailyBalance_BuildsAClosedLotOnlyUpToItsClosingDate()
    {
        var db = await Seed(NewAddress(StorageAddressStatus.Closed, Day), Movement(Day));
        var builder = new StorageAddressesDailyBalanceBuilderService(db);

        await builder.BuildAsync(Day);
        await builder.BuildAsync(Day.AddDays(1));

        var dates = await db.Context.StorageDailyBalances.Select(x => x.BalanceDate).ToListAsync();
        Assert.Equal([Day], dates);
    }

    // ---- Encerrar ----

    [Fact]
    public async Task Close_MarksTheLotClosedWithTheDate()
    {
        var db = await Seed(NewAddress(), Movement(Day));

        await new StorageAddressesCloseService(db).ExecuteAsync(Lot, Day.AddHours(15), "tester");

        var lot = await db.Context.StorageAddresses.SingleAsync();
        Assert.Equal(StorageAddressStatus.Closed, lot.Status);
        Assert.Equal(Day, lot.ClosingDate);
        Assert.Equal("tester", lot.UpdatedBy);
    }

    [Fact]
    public async Task Close_RefusesAFutureDate()
    {
        var db = await Seed(NewAddress());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageAddressesCloseService(db).ExecuteAsync(Lot, DateTime.Today.AddDays(1), "tester"));

        Assert.Contains("futura", ex.Message);
    }

    [Fact]
    public async Task Close_RefusesALotAlreadyClosed()
    {
        var db = await Seed(NewAddress(StorageAddressStatus.Closed, Day));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageAddressesCloseService(db).ExecuteAsync(Lot, Day, "tester"));
    }

    [Fact]
    public async Task Close_RefusesAnUnknownLot()
    {
        var db = await Seed(NewAddress());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new StorageAddressesCloseService(db).ExecuteAsync("NOPE", Day, "tester"));
    }

    [Fact]
    public async Task Close_RefusesWhenThereIsAMovementAfterTheDate()
    {
        var db = await Seed(NewAddress(),
            Movement(Day),
            Movement(Day.AddDays(2), StorageTransactionType.Shipment, StorageTransactionsStatus.Pending));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageAddressesCloseService(db).ExecuteAsync(Lot, Day, "tester"));

        Assert.Contains("movimenta", ex.Message);
    }

    [Fact]
    public async Task Close_IgnoresCancelledMovementsAfterTheDate()
    {
        var db = await Seed(NewAddress(),
            Movement(Day),
            Movement(Day.AddDays(2), StorageTransactionType.Shipment, StorageTransactionsStatus.Cancelled));

        await new StorageAddressesCloseService(db).ExecuteAsync(Lot, Day, "tester");

        Assert.Equal(StorageAddressStatus.Closed, (await db.Context.StorageAddresses.SingleAsync()).Status);
    }

    /// <summary>
    /// Encerrar com data retroativa desfaz o que o job diário já calculou depois dela e ainda não
    /// foi faturado: saldo diário, cobrança e a quebra técnica automática.
    /// </summary>
    [Fact]
    public async Task Close_RemovesUninvoicedCalculationsAfterTheDate()
    {
        var after = Day.AddDays(1);
        var db = await Seed(NewAddress(),
            Movement(Day),
            Movement(after, StorageTransactionType.TechnicalLoss, comments: "QUEBRA_TECNICA_20260902_20260902"),
            Balance(Day), Balance(after),
            Charge(Day), Charge(after));

        await new StorageAddressesCloseService(db).ExecuteAsync(Lot, Day, "tester");

        Assert.Equal([Day], await db.Context.StorageDailyBalances.Select(x => x.BalanceDate).ToListAsync());
        Assert.Equal([Day], await db.Context.StorageCharges.Select(x => x.PeriodEnd).ToListAsync());
        Assert.False(await db.Context.StorageTransactions
            .AnyAsync(x => x.TransactionType == StorageTransactionType.TechnicalLoss));
    }

    [Fact]
    public async Task Close_RefusesWhenAnInvoicedChargeGoesPastTheDate()
    {
        var db = await Seed(NewAddress(), Movement(Day), Charge(Day.AddDays(1), invoiced: true));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageAddressesCloseService(db).ExecuteAsync(Lot, Day, "tester"));

        Assert.Contains("faturad", ex.Message);
    }

    // ---- Reabrir ----

    [Fact]
    public async Task Reopen_ClearsTheClosingDate()
    {
        var db = await Seed(NewAddress(StorageAddressStatus.Closed, Day));

        await new StorageAddressesReopenService(db).ExecuteAsync(Lot, "tester");

        var lot = await db.Context.StorageAddresses.SingleAsync();
        Assert.Equal(StorageAddressStatus.Open, lot.Status);
        Assert.Null(lot.ClosingDate);
    }

    [Fact]
    public async Task Reopen_RefusesAnOpenLot()
    {
        var db = await Seed(NewAddress());

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageAddressesReopenService(db).ExecuteAsync(Lot, "tester"));
    }

    // ---- Edição comum não encerra nem reabre ----

    [Fact]
    public async Task Update_KeepsStatusAndClosingDate()
    {
        var db = await Seed(NewAddress(StorageAddressStatus.Closed, Day));

        // Mesmo objeto rastreado, como no PATCH OData (Delta aplicado antes do serviço).
        var tracked = await db.Context.StorageAddresses.SingleAsync();
        tracked.Status = StorageAddressStatus.Open;
        tracked.ClosingDate = null;
        tracked.Description = "Nova descrição";

        await new StorageAddressesUpdateService(
                db.Context,
                new FakeBusinessPartnerService(),
                new FakeItemService(),
                new FakeWarehouseService(),
                new TestLogger<StorageAddressesUpdateService>())
            .ExecuteAsync(Lot, tracked, "tester");

        db.Context.ChangeTracker.Clear();
        var lot = await db.Context.StorageAddresses.SingleAsync();
        Assert.Equal(StorageAddressStatus.Closed, lot.Status);
        Assert.Equal(Day, lot.ClosingDate);
        Assert.Equal("Nova descrição", lot.Description);
    }
}
