using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Cenário das operações de bolsa: corretora (conta 1 e 3), banco (conta 2) e instrumentos dos exemplos.</summary>
internal static class MarketFuturesTestHelpers
{
    public const string Manage = PermissionCodes.MarketManage;
    public const string Soy = "ZSF7";
    public const string Corn = "CCMK7";
    public const string Dollar = "DOLF7";
    public const string SoyMar = "ZSH7";

    public static readonly Guid PurchaseKey = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SalesKey = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Agora dos testes: 06/11/2026 15:00 UTC.</summary>
    public static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));

    public static MarketFuturesTradeService TradeService(UnitOfWork db, params string[] granted) =>
        TradeService(db, new KeyedAsyncLock(), granted);

    public static MarketFuturesTradeService TradeService(UnitOfWork db, KeyedAsyncLock locks, params string[] granted)
    {
        var permissions = new FakeUserPermissions(granted.Length == 0 ? [Manage] : granted);
        return new MarketFuturesTradeService(db.Context, new MarketPermissionGuard(permissions), Clock(), locks,
            new FakeCompanyContext("0001"));
    }

    public static MarketDerivativesPositionService PositionService(UnitOfWork db) => new(db.Context);

    public static MarketFuturesTradeInput Input(
        MarketTradeSide side, int lots, decimal price, string instrument = Soy, int accountId = 1,
        DateTime? tradedAt = null, Guid? purchaseKey = null, Guid? salesKey = null,
        MarketFuturesTradeReason reason = MarketFuturesTradeReason.Normal) =>
        new(tradedAt ?? new DateTime(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc), accountId, instrument, side, lots, price,
            10m, CurrencyType.Usd, reason, "REF-1", purchaseKey, salesKey, null);

    public static MarketInstrument Instrument(string code, MarketQuoteUnit unit, decimal size, decimal tonFactor,
        MarketInstrumentType type = MarketInstrumentType.Future, bool active = true) => new()
    {
        Code = code, Description = code, Commodity = unit == MarketQuoteUnit.BrlPerUsd ? "USD" : "SOY",
        Type = type, QuoteUnit = unit, ContractSize = size, TonFactor = tonFactor, Active = active,
        ExpirationDate = new DateTime(2027, 1, 14),
    };

    public static async Task<UnitOfWork> SeedAsync(string? databaseName = null)
    {
        var db = TestDb.CreateUnitOfWork(databaseName ?? Guid.NewGuid().ToString());
        db.Context.MarketCounterparties.AddRange(
            new MarketCounterparty
            {
                Id = 1, Name = "XP Corretora", Type = MarketCounterpartyType.Broker,
                Accounts =
                [
                    new MarketCounterpartyAccount { Id = 1, CounterpartyId = 1, Code = "C-001" },
                    new MarketCounterpartyAccount { Id = 3, CounterpartyId = 1, Code = "C-002" },
                    new MarketCounterpartyAccount { Id = 4, CounterpartyId = 1, Code = "C-OFF", Active = false },
                ],
            },
            new MarketCounterparty
            {
                Id = 2, Name = "Banco Alfa", Type = MarketCounterpartyType.Bank,
                Accounts = [new MarketCounterpartyAccount { Id = 2, CounterpartyId = 2, Code = "B-001" }],
            });
        db.Context.MarketInstruments.AddRange(
            Instrument(Soy, MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m),
            Instrument(SoyMar, MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m),
            Instrument(Corn, MarketQuoteUnit.BrlPerBag, 450m, 16.666667m),
            Instrument(Dollar, MarketQuoteUnit.BrlPerUsd, 50000m, 0m),
            Instrument("SOYSPOT", MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m, MarketInstrumentType.Spot),
            Instrument("ZSOFF", MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m, active: false),
            Instrument("ZSZERO", MarketQuoteUnit.UsCentsPerBushel, 0m, 0.36745m));
        db.Context.PurchaseContracts.Add(new PurchaseContract
        {
            Key = PurchaseKey, Code = "CC000010", CardCode = "F001", ItemCode = "SOJA", BranchCode = "01",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "F001",
        });
        db.Context.SalesContracts.Add(new SalesContract
        {
            Key = SalesKey, Code = "CV000020", CardCode = "C001", ItemCode = "SOJA", BranchCode = "01",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026",
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return db;
    }
}
