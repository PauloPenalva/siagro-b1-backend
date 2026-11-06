using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketTradeTicketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Alçada da boleta pelo limite de exposição (a boleta é COMPRA: soma toneladas compradas).</summary>
public class MarketTradeTicketLimitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // Linha da grade: SOJA, entrega até 31/03/2027 → regra mar → mai (05/2027). Boleta padrão = 100 t.
    private static async Task<UnitOfWork> SeedAsync(bool mapItem = true, decimal existingKg = 950_000m, params MarketExposureLimit[] limits)
    {
        var db = await SeedTicketScenarioAsync(null, null);
        var c = db.Context;
        c.MarketCommodities.Add(new MarketCommodity { Code = "SOY", Name = "Soja" });
        if (mapItem) c.MarketCommodityItems.Add(new MarketCommodityItem { CommodityCode = "SOY", ItemCode = "SOJA" });
        c.MarketCommodityMonths.Add(new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = 3, FuturesMonth = 5 });
        c.PurchaseContracts.Add(new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-EXIST", CardCode = "F001", CardName = "PRODUTOR", ItemCode = "SOJA", BranchCode = "01",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "01", Type = ContractType.Fixed,
            Status = ContractStatus.Approved, TotalVolume = existingKg, StandardPrice = 2m, StandardCurrency = CurrencyType.Brl,
            DeliveryEndDate = new DateTime(2027, 3, 31),
        });
        c.MarketExposureLimits.AddRange(limits);
        await db.SaveChangesAsync();
        c.ChangeTracker.Clear();
        return db;
    }

    private static MarketExposureLimit Limit(decimal maxLong, int? year = null, int? month = null, bool active = true) => new()
    {
        Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", Year = year, Month = month, MaxLong = maxLong, Active = active,
    };

    private static MarketPositionService Position(UnitOfWork db)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        var physical = new MarketPhysicalExposureService(db.Context, new MarketExposureCache(clock), new FakeCompanyContext("0001"), clock);
        return new MarketPositionService(db.Context, physical, new MarketDerivativesPositionService(db.Context), clock);
    }

    private static Task<SiagroB1.Domain.Entities.Market.MarketTradeTicket> CreateAsync(UnitOfWork db, MarketPositionService? position = null) =>
        TicketServiceWithPosition(db, position ?? Position(db)).CreateAsync(Input(), "comprador", Today, Ct);

    [Fact]
    public async Task Create_ExceedsCommodityTotalLimit_RequiresBoard()
    {
        var db = await SeedAsync(limits: Limit(1000m));

        var ticket = await CreateAsync(db);

        Assert.True(ticket.RequiresBoardApproval);
        Assert.Equal("Estoura o limite de exposição de Soja", ticket.BoardApprovalReason);
    }

    [Fact]
    public async Task Create_ExceedsMonthBucketLimit_RequiresBoard()
    {
        var db = await SeedAsync(limits: [Limit(5000m), Limit(1000m, 2027, 5)]);

        var ticket = await CreateAsync(db);

        Assert.True(ticket.RequiresBoardApproval);
        Assert.Equal("Estoura o limite de exposição de Soja", ticket.BoardApprovalReason);
    }

    [Fact]
    public async Task Create_WithinLimits_OrOtherMonthOrInactive_NoBoard()
    {
        var db = await SeedAsync(limits: [Limit(2000m), Limit(1000m, 2027, 7), Limit(10m, active: false)]);

        var ticket = await CreateAsync(db);

        Assert.False(ticket.RequiresBoardApproval);
        Assert.Null(ticket.BoardApprovalReason);
    }

    [Fact]
    public async Task Create_NoLimit_NoBoard()
    {
        var db = await SeedAsync();

        Assert.False((await CreateAsync(db)).RequiresBoardApproval);
    }

    [Fact]
    public async Task Create_ItemWithoutCommodity_NoBoard()
    {
        var db = await SeedAsync(mapItem: false, limits: Limit(10m));

        Assert.False((await CreateAsync(db)).RequiresBoardApproval);
    }

    [Fact]
    public async Task Create_PositionFails_WithActiveLimit_RequiresBoardForSafety()
    {
        var db = await SeedAsync(limits: Limit(1000m));

        var ticket = await CreateAsync(db, new ThrowingPositionService(db));

        Assert.True(ticket.RequiresBoardApproval);
        Assert.Equal("Não foi possível conferir o limite de exposição de Soja", ticket.BoardApprovalReason);
    }

    private sealed class ThrowingPositionService(UnitOfWork db) : MarketPositionService(db.Context,
        new MarketPhysicalExposureService(db.Context, new MarketExposureCache(TimeProvider.System), new FakeCompanyContext("0001"), TimeProvider.System),
        new MarketDerivativesPositionService(db.Context), TimeProvider.System)
    {
        public override Task<MarketPositionSnapshot> GetSnapshotAsync(DateOnly today, CancellationToken ct, bool bypassCache = false) =>
            throw new InvalidOperationException("falha simulada");
    }

    // ---- B4: boletas abertas contam e a aprovação reconfere ----

    private static async Task AddLimitAsync(UnitOfWork db, MarketExposureLimit limit)
    {
        db.Context.MarketExposureLimits.Add(limit);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Create_OpenTicketsCount_SecondOf600TonsRequiresBoard()
    {
        var db = await SeedAsync(existingKg: 0m, limits: Limit(1000m));
        var service = TicketServiceWithPosition(db, Position(db));

        var first = await service.CreateAsync(Input(volumeKg: 600_000m), "comprador", Today, Ct);
        var second = await service.CreateAsync(Input(volumeKg: 600_000m), "comprador", Today, Ct);

        Assert.False(first.RequiresBoardApproval);
        Assert.True(second.RequiresBoardApproval);
        Assert.Equal("Estoura o limite de exposição de Soja", second.BoardApprovalReason);
    }

    [Fact]
    public async Task Create_OpenTicketsOfMonthBucketCount_CanceledDoNot()
    {
        var db = await SeedAsync(existingKg: 0m, limits: Limit(1000m, 2027, 5));
        var service = TicketServiceWithPosition(db, Position(db), null, PermissionCodes.MarketTicketApprove);

        var first = await service.CreateAsync(Input(volumeKg: 600_000m), "comprador", Today, Ct);
        await service.CancelAsync(first.Id, "comprador", Ct);
        var second = await service.CreateAsync(Input(volumeKg: 600_000m), "comprador", Today, Ct);
        var third = await service.CreateAsync(Input(volumeKg: 600_000m), "comprador", Today, Ct);

        Assert.False(second.RequiresBoardApproval);
        Assert.True(third.RequiresBoardApproval);
    }

    [Fact]
    public async Task Approve_LimitCreatedAfterTicket_ApproverNotBoard_GoesToBoardWithoutContract()
    {
        var db = await SeedAsync();
        var created = await CreateAsync(db);
        Assert.False(created.RequiresBoardApproval);
        await AddLimitAsync(db, Limit(1000m));

        var ticket = await TicketServiceWithPosition(db, Position(db), null, PermissionCodes.MarketTicketApprove)
            .ApproveAsync(created.Id, "aprovador", Ct);

        Assert.Equal(MarketTradeTicketStatus.PendingBoard, ticket.Status);
        Assert.True(ticket.RequiresBoardApproval);
        Assert.Equal("Estoura o limite de exposição de Soja", ticket.BoardApprovalReason);
        Assert.Equal("aprovador", ticket.ApprovedBy);
        Assert.Null(ticket.PurchaseContractKey);
        Assert.Equal(1, db.Context.PurchaseContracts.Count());
    }

    [Fact]
    public async Task Approve_LimitCreatedAfterTicket_BoardApprover_GeneratesContract()
    {
        var db = await SeedAsync();
        var created = await CreateAsync(db);
        await AddLimitAsync(db, Limit(1000m));

        var ticket = await TicketServiceWithPosition(db, Position(db), null,
                PermissionCodes.MarketTicketApprove, PermissionCodes.MarketBoardApprove)
            .ApproveAsync(created.Id, "diretor", Ct);

        Assert.Equal(MarketTradeTicketStatus.Approved, ticket.Status);
        Assert.True(ticket.RequiresBoardApproval);
        Assert.Equal("diretor", ticket.BoardApprovedBy);
        Assert.NotNull(ticket.PurchaseContractKey);
        Assert.Equal(2, db.Context.PurchaseContracts.Count());
    }

    [Fact]
    public async Task Approve_WithinLimit_OwnTicketNotCountedTwice_GeneratesContract()
    {
        // 950 t + 50 t da própria boleta = 1.000 t: não passa do limite (contada duas vezes daria 1.050).
        var db = await SeedAsync(limits: Limit(1000m));
        var created = await TicketServiceWithPosition(db, Position(db)).CreateAsync(Input(volumeKg: 50_000m), "comprador", Today, Ct);
        Assert.False(created.RequiresBoardApproval);

        var ticket = await TicketServiceWithPosition(db, Position(db), null, PermissionCodes.MarketTicketApprove)
            .ApproveAsync(created.Id, "aprovador", Ct);

        Assert.Equal(MarketTradeTicketStatus.Approved, ticket.Status);
        Assert.False(ticket.RequiresBoardApproval);
    }
}
