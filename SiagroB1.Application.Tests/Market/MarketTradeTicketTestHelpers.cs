using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Pricing;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketPricingTestHelpers;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Cenário da boleta: linha S1 do oráculo (137,67 R$/sc), cadastros mestres e os serviços montados.</summary>
internal static class MarketTradeTicketTestHelpers
{
    public static readonly DateOnly Today = new(2026, 11, 6);
    public const string City = "4104808";
    public const int LineId = 1;

    public static async Task<UnitOfWork> SeedTicketScenarioAsync(decimal? boardTons = 1000m, decimal? boardDeviation = 2m, string? databaseName = null)
    {
        var zs = Usd(); zs.TonFactor = 0.36745m;
        var db = await SeedPricingAsync(databaseName, zs, SpotFx(), Curve());
        var c = db.Context;
        c.MarketTradingSettings.Add(new MarketTradingSettings
        {
            SpotFxInstrumentCode = "DOL COM", BoardApprovalVolumeTons = boardTons, BoardApprovalDeviationPercent = boardDeviation,
            ContractUnitOfMeasureCode = "KG", ContractFreightUmCode = "TON",
        });
        c.MarketQuotes.AddRange(Quote("ZSF7", 1188.25m), Quote("DOL COM", 5.4m), Quote("CURVA 30D", 5.465m));
        c.MarketFreightRates.Add(new MarketFreightRate
        {
            MunicipalityCode = City, DestinationId = 1, RatePerTon = 180, ValidFrom = new DateTime(2026, 1, 1),
        });
        var line = Line("ZSF7", "CURVA 30D");
        line.Id = LineId;
        line.Premium = 50; line.ElevationCost = 8; line.FxDiscount = 0.02m;
        line.DailyInterestPercent = 0.033m; line.MarginPercent = 1.5m; line.TechnicalLossPercent = 0.3m;
        line.RuralTaxPercent = 1.5m; line.FreightCoefficientPercent = 10; line.DiscountValue = 0.5m; line.DiscountIsPercent = true;
        line.DeliveryStartDate = new DateTime(2027, 2, 1); line.DeliveryEndDate = new DateTime(2027, 3, 31);
        c.MarketPriceTableLines.Add(line);
        c.Branchs.Add(new Branch { Code = "0001", BranchName = "MATRIZ" });
        c.LogisticRegions.Add(new LogisticRegion { Code = "OESTE", Name = "Oeste" });
        await db.SaveChangesAsync();
        c.ChangeTracker.Clear();
        return db;
    }

    private static MarketQuote Quote(string code, decimal last) => new()
    {
        InstrumentCode = code, Last = last, QuotedAt = new DateTime(2026, 11, 6, 12, 0, 0), Source = MarketQuoteSource.Cma,
    };

    public static MarketTradeTicketInput Input(decimal volumeKg = 100_000m, decimal price = 137.67m,
        string card = "F0001", string warehouse = "01", int agent = 1) =>
        new(LineId, City, card, volumeKg, price, warehouse, agent, "0001", "OESTE", "obs");

    public static MarketTradeTicketService TicketService(UnitOfWork db, params string[] granted) =>
        TicketService(db, new KeyedAsyncLock(), granted);

    public static MarketTradeTicketService TicketService(UnitOfWork db, KeyedAsyncLock locks, params string[] granted) =>
        TicketServiceWithPosition(db, null, locks, granted);

    /// <summary>Boleta com o painel de posição ligado (regra de alçada por limite de exposição).</summary>
    public static MarketTradeTicketService TicketServiceWithPosition(UnitOfWork db, SiagroB1.Application.Services.Market.Position.MarketPositionService? position,
        KeyedAsyncLock? locks = null, params string[] granted)
    {
        locks ??= new KeyedAsyncLock();
        var permissions = new FakeUserPermissions(granted);
        return new MarketTradeTicketService(
            db.Context, new MarketPriceGridService(db.Context, FreightService(db)), ContractService(db),
            new FakeBusinessPartnerService(names: new() { ["F0001"] = "PRODUTOR TESTE" }),
            new FakeWarehouseService(new() { ["01"] = "ARM 01" }),
            new FakeAgentService(new() { [1] = "AGENTE" }),
            permissions, new MarketPermissionGuard(permissions),
            new ManualTimeProvider(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero)),
            locks, new FakeCompanyContext("0001"), position);
    }

    public static MarketTradeTicketContractService ContractService(UnitOfWork db) => new(
        db.Context,
        CreateService(db),
        new PurchaseContractsSendApprovalService(db.Context, TestNotificationOutbox.For(db.Context)),
        new PurchaseContractsApprovalService(db.Context, TestNotificationOutbox.For(db.Context),
            FinancialDocumentTestServices.Generate(db.Context)));

    public static PurchaseContractsCreateService CreateService(UnitOfWork db) =>
        new PurchaseContractsCreateService(db.Context,
            new FakeBusinessPartnerService(names: new() { ["F0001"] = "PRODUTOR TESTE" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeAgentService(new() { [1] = "AGENTE" }),
            new FakeWarehouseService(new() { ["01"] = "ARM 01" }),
            new FakeDocNumberSequenceService(), TestNotificationOutbox.For(db.Context),
            new FakeStringLocalizer<Resource>(), NullLogger<PurchaseContractsCreateService>.Instance);
}
