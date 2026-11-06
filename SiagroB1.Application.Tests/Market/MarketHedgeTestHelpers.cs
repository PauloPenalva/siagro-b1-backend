using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Montagem de serviços e cenário das contrapartes/contas/extratos (parte 3 — hedges).</summary>
internal static class MarketHedgeTestHelpers
{
    public static MarketCounterpartyService CounterpartyService(UnitOfWork db) =>
        new(db.Context, new TestLogger<MarketCounterpartyService>());

    public static MarketCounterpartyAccountService AccountService(UnitOfWork db) =>
        new(db.Context, new TestLogger<MarketCounterpartyAccountService>());

    public static MarketBrokerStatementService StatementService(UnitOfWork db) =>
        new(db.Context, new TestLogger<MarketBrokerStatementService>());

    public static MarketCounterparty Broker(string name = "XP Corretora") => new() { Name = name, Type = MarketCounterpartyType.Broker };

    public static MarketCounterparty Bank(string name = "Banco Alfa") => new() { Name = name, Type = MarketCounterpartyType.Bank };

    public static MarketCounterpartyAccount Account(int counterpartyId, string code = "C-001") =>
        new() { CounterpartyId = counterpartyId, Code = code };

    public static MarketBrokerStatement Statement(int accountId, DateTime? date = null) => new()
    {
        AccountId = accountId, Date = date ?? new DateTime(2026, 11, 5), DailyAdjustment = -150.5m,
        MarginRequired = 1000m, MarginDeposited = 1200m, Currency = CurrencyType.Usd,
    };

    /// <summary>Corretora (id 1) com conta 1, banco (id 2) com conta 2.</summary>
    public static async Task<UnitOfWork> SeedCounterpartiesAsync(string? databaseName = null)
    {
        var db = TestDb.CreateUnitOfWork(databaseName ?? Guid.NewGuid().ToString());
        var broker = Broker();
        broker.Id = 1;
        broker.Accounts = [new MarketCounterpartyAccount { Id = 1, CounterpartyId = 1, Code = "C-001" }];
        var bank = Bank();
        bank.Id = 2;
        bank.Accounts = [new MarketCounterpartyAccount { Id = 2, CounterpartyId = 2, Code = "B-001" }];
        db.Context.MarketCounterparties.AddRange(broker, bank);
        db.Context.MarketInstruments.Add(MarketTestHelpers.Instrument("ZSF7"));
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return db;
    }

    public static async Task UseAccountAsync(UnitOfWork db, string table, int accountId)
    {
        switch (table)
        {
            case "trade":
                db.Context.MarketFuturesTrades.Add(new MarketFuturesTrade { AccountId = accountId, InstrumentCode = "ZSF7" });
                break;
            case "fx":
                db.Context.MarketFxHedges.Add(new MarketFxHedge { AccountId = accountId });
                break;
            case "efp":
                db.Context.MarketEfps.Add(new MarketEfp { AccountId = accountId, InstrumentCode = "ZSF7", ContractCode = "CT-1" });
                break;
            default:
                db.Context.MarketBrokerStatements.Add(Statement(accountId));
                break;
        }

        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
