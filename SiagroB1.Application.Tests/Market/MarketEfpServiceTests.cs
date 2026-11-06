using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketFuturesTestHelpers;

namespace SiagroB1.Application.Tests.Market;

/// <summary>
/// EFP contra os serviços REAIS de fixação de compra e venda (criação, aprovação, estorno, exclusão) e o gerador real
/// de título provisório, sobre InMemory. Falhas são injetadas no SaveChanges entre cada par de passos; cada tentativa
/// usa um contexto novo sobre o mesmo banco (como um escopo de requisição).
/// </summary>
public class MarketEfpServiceTests
{
    private static readonly Guid PafPurchaseKey = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PafSalesKey = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid UsdPurchaseKey = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid FixedPurchaseKey = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid DraftPurchaseKey = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid SackPurchaseKey = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid NoDuePurchaseKey = Guid.Parse("99999999-9999-9999-9999-999999999999");

    /// <summary>Lança uma única vez no primeiro SaveChanges que tiver uma entrada que case com o predicado.</summary>
    private sealed class FailOnceInterceptor(Func<EntityEntry, bool> match) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries().Any(match))
            {
                Fired = true;
                throw new InvalidOperationException("Falha simulada.");
            }

            return new ValueTask<InterceptionResult<int>>(result);
        }
    }

    private static async Task<string> SeedEfpAsync()
    {
        var name = Guid.NewGuid().ToString();
        var db = await SeedAsync(name);
        PurchaseContract Purchase(Guid key, string code, ContractType type = ContractType.ToBeDetermined,
            ContractStatus status = ContractStatus.Approved, string uom = "KG", CurrencyType currency = CurrencyType.Brl,
            DateTime? cashFlow = null, bool noCashFlow = false) => new()
        {
            Key = key, Code = code, CardCode = "F0001", CardName = "PRODUTOR TESTE", ItemCode = "SOJA",
            ItemName = "SOJA GRAO", BranchCode = "01", UnitOfMeasureCode = uom, HarvestSeasonCode = "2026",
            DeliveryLocationCode = "01", TotalVolume = 1_000_000m, Type = type, Status = status,
            StandardCurrency = currency,
            StandardCashFlowDate = noCashFlow ? null : cashFlow ?? new DateTime(2026, 12, 15),
        };
        db.Context.PurchaseContracts.AddRange(
            Purchase(PafPurchaseKey, "CC000100"),
            Purchase(UsdPurchaseKey, "CC000101", currency: CurrencyType.Usd),
            Purchase(FixedPurchaseKey, "CC000102", type: ContractType.Fixed),
            Purchase(DraftPurchaseKey, "CC000103", status: ContractStatus.Draft),
            Purchase(SackPurchaseKey, "CC000104", uom: "SC"),
            Purchase(NoDuePurchaseKey, "CC000105", noCashFlow: true));
        db.Context.SalesContracts.Add(new SalesContract
        {
            Key = PafSalesKey, Code = "CV000200", CardCode = "C0001", CardName = "CLIENTE TESTE", ItemCode = "SOJA",
            BranchCode = "01", UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", TotalVolume = 500_000m,
            Type = ContractType.ToBeDetermined, Status = ContractStatus.Approved, StandardCurrency = CurrencyType.Brl,
            StandardCashFlowDate = new DateTime(2027, 1, 20),
        });
        await db.SaveChangesAsync();
        return name;
    }

    private static MarketEfpService Service(UnitOfWork db, KeyedAsyncLock? locks = null, params string[] granted)
    {
        var c = db.Context;
        var l = locks ?? new KeyedAsyncLock();
        var outbox = TestNotificationOutbox.For(c);
        var generate = FinancialDocumentTestServices.Generate(c);
        var cancel = FinancialDocumentTestServices.Cancel(c);
        var permissions = new FakeUserPermissions(granted.Length == 0 ? [Manage] : granted);
        var pFixed = new PurchaseContractsFixedVolumeService(c);
        var pLog = new PurchaseContractsChangeLogService(c);
        var sFixed = new SalesContractsFixedVolumeService(c);
        var sLog = new SalesContractsChangeLogService(c);
        return new MarketEfpService(
            c, new MarketPermissionGuard(permissions), Clock(), l, new FakeCompanyContext("0001"),
            TradeService(db, l, granted.Length == 0 ? [Manage] : granted),
            new MarketEfpFixations(
                new PurchaseContractsPriceFixationCreateService(c, pFixed, pLog, outbox,
                    new TestLogger<PurchaseContractsPriceFixationCreateService>()),
                new PurchaseContractsPriceFixationsApprovalService(c, pFixed, pLog, outbox, generate),
                new PurchaseContractsPriceFixationsCancelService(c, pFixed, pLog, outbox, cancel),
                new PurchaseContractsPriceFixationDeleteService(c, pFixed, pLog,
                    new TestLogger<PurchaseContractsPriceFixationDeleteService>()),
                new SalesContractsPriceFixationCreateService(c, sFixed, sLog, outbox,
                    new TestLogger<SalesContractsPriceFixationCreateService>()),
                new SalesContractsPriceFixationsApprovalService(c, sFixed, sLog, outbox, generate),
                new SalesContractsPriceFixationsCancelService(c, sFixed, sLog, outbox, cancel),
                new SalesContractsPriceFixationDeleteService(c, sFixed, sLog,
                    new TestLogger<SalesContractsPriceFixationDeleteService>())));
    }

    private static MarketEfpService Service(string name, IInterceptor? fail = null, KeyedAsyncLock? locks = null) =>
        Service(fail is null ? TestDb.CreateUnitOfWork(name) : TestDb.CreateUnitOfWork(name, fail), locks);

    private static MarketEfpInput SoyInput(Guid? contractKey = null, decimal volumeKg = 136_072.9m,
        decimal? fxRate = 5.40m, MarketEfpContractSide side = MarketEfpContractSide.Purchase,
        string instrument = Soy, int lots = 1, decimal price = 1190m, int accountId = 1,
        DateOnly? date = null, DateOnly? due = null, MarketTradeSide tradeSide = MarketTradeSide.Sell) =>
        new(date ?? new DateOnly(2026, 11, 5), accountId, instrument, tradeSide, lots, price, 60m, fxRate, side,
            contractKey ?? PafPurchaseKey, volumeKg, due, "obs");

    private static MarketEfpInput CornInput(Guid contractKey) =>
        new(new DateOnly(2026, 11, 5), 1, Corn, MarketTradeSide.Sell, 5, 68.00m, 1.50m, null,
            MarketEfpContractSide.Purchase, contractKey, 135_000m, null, null);

    private static async Task<UnitOfWork> Read(string name) => await Task.FromResult(TestDb.CreateUnitOfWork(name));

    private static async Task AssertCompletedOnceAsync(string name, int efpId, Guid contractKey, bool sales = false)
    {
        var c = (await Read(name)).Context;
        var efp = await c.MarketEfps.AsNoTracking().SingleAsync(e => e.Id == efpId);
        Assert.Equal(MarketEfpStatus.Active, efp.Status);
        var trade = Assert.Single(await c.MarketFuturesTrades.AsNoTracking().Where(t => t.EfpId == efpId).ToListAsync());
        Assert.Equal(trade.Id, efp.FuturesTradeId);
        Assert.Equal(MarketFuturesTradeReason.Efp, trade.Reason);
        Assert.NotNull(efp.FixationKey);
        if (sales)
        {
            var fixation = Assert.Single(await c.SalesContractsPriceFixations.AsNoTracking()
                .Where(f => f.SalesContractKey == contractKey).ToListAsync());
            Assert.Equal(efp.FixationKey, fixation.Key);
            Assert.Equal(PriceFixationStatus.Confirmed, fixation.Status);
        }
        else
        {
            var fixation = Assert.Single(await c.PurchaseContractsPriceFixations.AsNoTracking()
                .Where(f => f.PurchaseContractKey == contractKey).ToListAsync());
            Assert.Equal(efp.FixationKey, fixation.Key);
            Assert.Equal(PriceFixationStatus.Confirmed, fixation.Status);
        }

        Assert.Single(await c.FinancialDocuments.AsNoTracking().Where(d =>
            d.OriginKey == efp.FixationKey && d.Nature == FinancialDocumentNature.Provisional
                                           && d.Status != FinancialDocumentStatus.Canceled).ToListAsync());
    }

    // ---------- Preview ----------

    [Fact]
    public async Task Preview_Soy_ComputesPricePerKgAndPerBag()
    {
        var name = await SeedEfpAsync();
        var preview = await Service(name).PreviewAsync(SoyInput(), CancellationToken.None);

        Assert.Equal(2.48028750m, preview.FixationPricePerKg);
        Assert.Equal(148.82m, preview.FixationPricePerBag);
        Assert.Equal(CurrencyType.Brl, preview.Currency);
        Assert.Equal(-136.0729m, preview.TonsFutures);
        Assert.Equal(136.0729m, preview.TonsPhysical);
    }

    [Fact]
    public async Task Preview_CornB3_DividesBy60()
    {
        var name = await SeedEfpAsync();
        var preview = await Service(name).PreviewAsync(CornInput(PafPurchaseKey), CancellationToken.None);

        Assert.Equal(1.15833333m, preview.FixationPricePerKg);
        Assert.Equal(69.50m, preview.FixationPricePerBag);
        Assert.Equal(-135m, preview.TonsFutures);
        Assert.Equal(135m, preview.TonsPhysical);
    }

    [Fact]
    public async Task Preview_UsdInstrumentAndUsdContract_UsesRateOne()
    {
        var name = await SeedEfpAsync();
        var preview = await Service(name).PreviewAsync(SoyInput(UsdPurchaseKey, fxRate: 5.40m), CancellationToken.None);

        // (1190 + 60) × 0,36745 / 1000 = 0,45931250 US$/kg — o câmbio informado é ignorado.
        Assert.Equal(0.45931250m, preview.FixationPricePerKg);
        Assert.Equal(CurrencyType.Usd, preview.Currency);
    }

    // ---------- Validações ----------

    public static TheoryData<string> InvalidCases =>
    [
        "fixed", "draft", "uom", "missing-contract", "volume-zero", "volume-over", "account-inactive", "account-bank",
        "instrument-inactive", "instrument-spot", "instrument-dollar", "instrument-untradable", "lots", "price",
        "fx-missing", "brl-instrument-usd-contract", "due-missing", "future-date", "unknown-instrument",
    ];

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task Create_InvalidInput_ThrowsBusinessExceptionAndSavesNothing(string scenario)
    {
        var name = await SeedEfpAsync();
        var input = scenario switch
        {
            "fixed" => SoyInput(FixedPurchaseKey),
            "draft" => SoyInput(DraftPurchaseKey),
            "uom" => SoyInput(SackPurchaseKey),
            "missing-contract" => SoyInput(Guid.NewGuid()),
            "volume-zero" => SoyInput(volumeKg: 0m),
            "volume-over" => SoyInput(volumeKg: 1_000_000.001m),
            "account-inactive" => SoyInput(accountId: 4),
            "account-bank" => SoyInput(accountId: 2),
            "instrument-inactive" => SoyInput(instrument: "ZSOFF"),
            "instrument-spot" => SoyInput(instrument: "SOYSPOT"),
            "instrument-dollar" => SoyInput(instrument: Dollar),
            "instrument-untradable" => SoyInput(instrument: "ZSZERO"),
            "unknown-instrument" => SoyInput(instrument: "NOPE"),
            "lots" => SoyInput(lots: 0),
            "price" => SoyInput(price: 0m),
            "fx-missing" => SoyInput(fxRate: null),
            "brl-instrument-usd-contract" => CornInput(UsdPurchaseKey),
            "due-missing" => SoyInput(NoDuePurchaseKey),
            "future-date" => SoyInput(date: new DateOnly(2026, 11, 7)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            Service(name).CreateAsync(input, "ana", CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
        var c = (await Read(name)).Context;
        Assert.Empty(await c.MarketEfps.ToListAsync());
        Assert.Empty(await c.MarketFuturesTrades.ToListAsync());
    }

    [Fact]
    public async Task Create_VolumeAboveRemainingAfterOtherFixation_Throws()
    {
        var name = await SeedEfpAsync();
        var c = (await Read(name)).Context;
        c.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(), PurchaseContractKey = PafPurchaseKey, FixationVolume = 900_000m, FixationPrice = 2m,
            Status = PriceFixationStatus.InApproval,
        });
        await c.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            Service(name).CreateAsync(SoyInput(volumeKg: 100_000.001m), "ana", CancellationToken.None));
    }

    [Fact]
    public async Task Create_WithoutManagePermission_ThrowsForbidden()
    {
        var name = await SeedEfpAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(TestDb.CreateUnitOfWork(name), null, "MARKET_VIEW").CreateAsync(SoyInput(), "ana", CancellationToken.None));
    }

    // ---------- Criação ----------

    [Fact]
    public async Task Create_Purchase_CreatesTradeFixationAndProvisionalTitle()
    {
        var name = await SeedEfpAsync();
        var efp = await Service(name).CreateAsync(SoyInput(), "ana", CancellationToken.None);

        Assert.Equal(MarketEfpStatus.Active, efp.Status);
        Assert.Equal(2.48028750m, efp.FixationPrice);
        Assert.Equal("CC000100", efp.ContractCode);
        Assert.Equal(5.40m, efp.FxRate);
        Assert.Equal(new DateTime(2026, 12, 15), efp.FinancialDueDate);
        await AssertCompletedOnceAsync(name, efp.Id, PafPurchaseKey);

        var c = (await Read(name)).Context;
        var trade = await c.MarketFuturesTrades.AsNoTracking().SingleAsync();
        Assert.Equal(MarketTradeSide.Sell, trade.Side);
        Assert.Equal(1, trade.Lots);
        Assert.Equal(1190m, trade.Price);
        Assert.Equal(0m, trade.Fees);
        Assert.Equal(PafPurchaseKey, trade.PurchaseContractKey);
        Assert.Equal("CC000100", trade.ContractCode);
        // 05/11/2026 12:00 em São Paulo = 15:00 UTC.
        Assert.Equal(new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc), trade.TradedAt);

        var fixation = await c.PurchaseContractsPriceFixations.AsNoTracking().SingleAsync();
        Assert.Equal(136_072.9m, fixation.FixationVolume);
        Assert.Equal(2.48028750m, fixation.FixationPrice);
        Assert.Equal(new DateTime(2026, 11, 5), fixation.FixationDate);
        Assert.Equal(new DateTime(2026, 12, 15), fixation.FinancialDueDate);
        Assert.Equal($"EFP nº {efp.Id}", fixation.PaymentDetails);
        Assert.Equal("ana", fixation.CreatedBy);
        Assert.Equal("ana", fixation.ApprovedBy);
        Assert.Equal(136_072.9m, (await c.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == PafPurchaseKey)).FixedVolume);
    }

    [Fact]
    public async Task Create_Sales_UsesSalesServicesAndReceivable()
    {
        var name = await SeedEfpAsync();
        var efp = await Service(name).CreateAsync(
            SoyInput(PafSalesKey, side: MarketEfpContractSide.Sales, tradeSide: MarketTradeSide.Buy, due: new DateOnly(2027, 2, 1)),
            "ana", CancellationToken.None);

        await AssertCompletedOnceAsync(name, efp.Id, PafSalesKey, sales: true);
        var c = (await Read(name)).Context;
        var trade = await c.MarketFuturesTrades.AsNoTracking().SingleAsync();
        Assert.Equal(PafSalesKey, trade.SalesContractKey);
        Assert.Null(trade.PurchaseContractKey);
        var doc = await c.FinancialDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialDocumentOrigin.SalesContractPriceFixation, doc.OriginType);
        Assert.Equal(new DateTime(2027, 2, 1), efp.FinancialDueDate);
        Assert.Empty(await c.PurchaseContractsPriceFixations.ToListAsync());
    }

    [Fact]
    public async Task Create_UsdInstrumentUsdContract_StoresNullFxRate()
    {
        var name = await SeedEfpAsync();
        var efp = await Service(name).CreateAsync(SoyInput(UsdPurchaseKey), "ana", CancellationToken.None);
        Assert.Null(efp.FxRate);
        Assert.Equal(0.45931250m, efp.FixationPrice);
    }

    [Fact]
    public async Task Create_BrlInstrument_IgnoresFxRate()
    {
        var name = await SeedEfpAsync();
        var input = CornInput(PafPurchaseKey) with { FxRate = 5m };
        var efp = await Service(name).CreateAsync(input, "ana", CancellationToken.None);
        Assert.Null(efp.FxRate);
        Assert.Equal(1.15833333m, efp.FixationPrice);
    }

    [Fact]
    public async Task Create_DatedToday_UsesNowAsTradedAt()
    {
        var name = await SeedEfpAsync();
        await Service(name).CreateAsync(SoyInput(date: new DateOnly(2026, 11, 6)), "ana", CancellationToken.None);
        var trade = await (await Read(name)).Context.MarketFuturesTrades.AsNoTracking().SingleAsync();
        Assert.Equal(new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc), trade.TradedAt);
    }

    // ---------- Falha entre passos + retomada ----------

    public static TheoryData<string> FailurePoints =>
        ["trade-insert", "trade-id-store", "fixation-insert", "fixation-approval", "activate"];

    private static Func<EntityEntry, bool> FailureAt(string point, bool sales) => point switch
    {
        "trade-insert" => e => e.Entity is MarketFuturesTrade && e.State == EntityState.Added,
        "trade-id-store" => e => e.Entity is MarketEfp { FuturesTradeId: not null } && e.State == EntityState.Modified,
        "fixation-insert" => sales
            ? e => e.Entity is SalesContractPriceFixation && e.State == EntityState.Added
            : e => e.Entity is PurchaseContractPriceFixation && e.State == EntityState.Added,
        "fixation-approval" => sales
            ? e => e.Entity is SalesContractPriceFixation { Status: PriceFixationStatus.Confirmed } && e.State == EntityState.Modified
            : e => e.Entity is PurchaseContractPriceFixation { Status: PriceFixationStatus.Confirmed } && e.State == EntityState.Modified,
        "activate" => e => e.Entity is MarketEfp { Status: MarketEfpStatus.Active } && e.State == EntityState.Modified,
        _ => throw new ArgumentOutOfRangeException(nameof(point)),
    };

    [Theory]
    [MemberData(nameof(FailurePoints))]
    public async Task Create_FailsBetweenSteps_ResumeCompletesWithoutDuplicates_Purchase(string point)
    {
        var name = await SeedEfpAsync();
        var fail = new FailOnceInterceptor(FailureAt(point, sales: false));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Service(name, fail).CreateAsync(SoyInput(), "ana", CancellationToken.None));
        Assert.True(fail.Fired);

        var pending = await (await Read(name)).Context.MarketEfps.AsNoTracking().SingleAsync();
        Assert.Equal(MarketEfpStatus.Processing, pending.Status);
        Assert.NotNull(pending.FixationKey);

        var resumed = await Service(name).ResumeAsync(pending.Id, "ana", CancellationToken.None);
        Assert.Equal(MarketEfpStatus.Active, resumed.Status);
        await AssertCompletedOnceAsync(name, pending.Id, PafPurchaseKey);

        // Retomar um EFP já concluído não faz nada.
        await Service(name).ResumeAsync(pending.Id, "ana", CancellationToken.None);
        await AssertCompletedOnceAsync(name, pending.Id, PafPurchaseKey);
    }

    [Theory]
    [MemberData(nameof(FailurePoints))]
    public async Task Create_FailsBetweenSteps_ResumeCompletesWithoutDuplicates_Sales(string point)
    {
        var name = await SeedEfpAsync();
        var fail = new FailOnceInterceptor(FailureAt(point, sales: true));
        await Assert.ThrowsAnyAsync<Exception>(() => Service(name, fail).CreateAsync(
            SoyInput(PafSalesKey, side: MarketEfpContractSide.Sales, tradeSide: MarketTradeSide.Buy),
            "ana", CancellationToken.None));

        var pending = await (await Read(name)).Context.MarketEfps.AsNoTracking().SingleAsync();
        Assert.Equal(MarketEfpStatus.Processing, pending.Status);
        await Service(name).ResumeAsync(pending.Id, "ana", CancellationToken.None);
        await AssertCompletedOnceAsync(name, pending.Id, PafSalesKey, sales: true);
    }

    [Fact]
    public async Task Resume_Concurrent_CompletesOnce()
    {
        var name = await SeedEfpAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => Service(name, new FailOnceInterceptor(FailureAt("trade-insert", false)))
            .CreateAsync(SoyInput(), "ana", CancellationToken.None));
        var id = (await (await Read(name)).Context.MarketEfps.AsNoTracking().SingleAsync()).Id;

        var locks = new KeyedAsyncLock();
        var results = await Task.WhenAll(
            Task.Run(() => Service(name, null, locks).ResumeAsync(id, "ana", CancellationToken.None)),
            Task.Run(() => Service(name, null, locks).ResumeAsync(id, "bia", CancellationToken.None)));

        Assert.All(results, r => Assert.Equal(MarketEfpStatus.Active, r.Status));
        await AssertCompletedOnceAsync(name, id, PafPurchaseKey);
        Assert.Equal(0, locks.ActiveKeys);
    }

    [Fact]
    public async Task Create_IgnoresRequestCancellationAfterFirstSave()
    {
        var name = await SeedEfpAsync();
        using var cts = new CancellationTokenSource();
        var fail = new FailOnceInterceptor(e =>
        {
            // Cancela a requisição logo depois da primeira gravação do EFP.
            if (e.Entity is MarketFuturesTrade && e.State == EntityState.Added) cts.Cancel();
            return false;
        });
        var efp = await Service(name, fail).CreateAsync(SoyInput(), "ana", cts.Token);
        Assert.Equal(MarketEfpStatus.Active, efp.Status);
    }

    [Fact]
    public async Task Resume_UnknownId_ThrowsKeyNotFound()
    {
        var name = await SeedEfpAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(name).ResumeAsync(999, "ana", CancellationToken.None));
    }

    [Fact]
    public async Task Resume_FixationRejectedOutside_ThrowsBusiness()
    {
        var name = await SeedEfpAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => Service(name, new FailOnceInterceptor(FailureAt("fixation-approval", false)))
            .CreateAsync(SoyInput(), "ana", CancellationToken.None));
        var c = (await Read(name)).Context;
        var fixation = await c.PurchaseContractsPriceFixations.SingleAsync();
        fixation.Status = PriceFixationStatus.Rejected;
        await c.SaveChangesAsync();
        var id = (await c.MarketEfps.AsNoTracking().SingleAsync()).Id;

        await Assert.ThrowsAsync<BusinessException>(() => Service(name).ResumeAsync(id, "ana", CancellationToken.None));
        Assert.Single(await (await Read(name)).Context.PurchaseContractsPriceFixations.ToListAsync());
    }

    // ---------- Cancelamento ----------

    private static async Task<MarketEfp> CreateActiveAsync(string name, bool sales = false) =>
        await Service(name).CreateAsync(sales
            ? SoyInput(PafSalesKey, side: MarketEfpContractSide.Sales, tradeSide: MarketTradeSide.Buy)
            : SoyInput(), "ana", CancellationToken.None);

    private static async Task AssertCanceledCleanAsync(string name, int id)
    {
        var c = (await Read(name)).Context;
        var efp = await c.MarketEfps.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(MarketEfpStatus.Canceled, efp.Status);
        Assert.Equal("bia", efp.CanceledBy);
        Assert.NotNull(efp.CanceledAt);
        Assert.Empty(await c.MarketFuturesTrades.ToListAsync());
        Assert.Empty(await c.PurchaseContractsPriceFixations.ToListAsync());
        Assert.Empty(await c.SalesContractsPriceFixations.ToListAsync());
        Assert.All(await c.FinancialDocuments.AsNoTracking().ToListAsync(),
            d => Assert.Equal(FinancialDocumentStatus.Canceled, d.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_Active_ReversesDeletesAndMarksCanceled(bool sales)
    {
        var name = await SeedEfpAsync();
        var efp = await CreateActiveAsync(name, sales);

        var canceled = await Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None);
        Assert.Equal(MarketEfpStatus.Canceled, canceled.Status);
        await AssertCanceledCleanAsync(name, efp.Id);
        var c = (await Read(name)).Context;
        Assert.Empty(await c.MarketFuturesMatches.ToListAsync());
        if (!sales)
            Assert.Equal(0m, (await c.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == PafPurchaseKey)).FixedVolume);
    }

    [Fact]
    public async Task Cancel_RematchesFifoOfThePair()
    {
        var name = await SeedEfpAsync();
        // Compra normal aberta antes; a venda do EFP casa com ela; cancelar devolve a compra a aberta.
        var db = TestDb.CreateUnitOfWork(name);
        await TradeService(db).CreateAsync(Input(MarketTradeSide.Buy, 1, 1180m,
            tradedAt: new DateTime(2026, 11, 4, 14, 0, 0, DateTimeKind.Utc)), "ana", CancellationToken.None);
        var efp = await CreateActiveAsync(name);
        Assert.Single(await (await Read(name)).Context.MarketFuturesMatches.ToListAsync());

        await Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None);
        var c = (await Read(name)).Context;
        Assert.Empty(await c.MarketFuturesMatches.ToListAsync());
        Assert.Equal(1, (await c.MarketFuturesTrades.AsNoTracking().SingleAsync()).OpenLots);
    }

    [Fact]
    public async Task Cancel_Processing_WithFixationInApproval_DeletesIt()
    {
        var name = await SeedEfpAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => Service(name, new FailOnceInterceptor(FailureAt("fixation-approval", false)))
            .CreateAsync(SoyInput(), "ana", CancellationToken.None));
        var id = (await (await Read(name)).Context.MarketEfps.AsNoTracking().SingleAsync()).Id;

        await Service(name).CancelAsync(id, "bia", CancellationToken.None);
        await AssertCanceledCleanAsync(name, id);
    }

    [Fact]
    public async Task Cancel_Processing_BeforeAnything_OnlyMarksCanceled()
    {
        var name = await SeedEfpAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => Service(name, new FailOnceInterceptor(FailureAt("trade-insert", false)))
            .CreateAsync(SoyInput(), "ana", CancellationToken.None));
        var id = (await (await Read(name)).Context.MarketEfps.AsNoTracking().SingleAsync()).Id;

        await Service(name).CancelAsync(id, "bia", CancellationToken.None);
        await AssertCanceledCleanAsync(name, id);
    }

    public static TheoryData<string> CancelFailurePoints => ["fixation-delete", "trade-delete", "mark-canceled"];

    [Theory]
    [MemberData(nameof(CancelFailurePoints))]
    public async Task Cancel_Interrupted_CanBeRepeated_AndBlocksResume(string point)
    {
        var name = await SeedEfpAsync();
        var efp = await CreateActiveAsync(name);
        Func<EntityEntry, bool> match = point switch
        {
            "fixation-delete" => e => e.Entity is PurchaseContractPriceFixation && e.State == EntityState.Deleted,
            "trade-delete" => e => e.Entity is MarketFuturesTrade && e.State == EntityState.Deleted,
            _ => e => e.Entity is MarketEfp { Status: MarketEfpStatus.Canceled } && e.State == EntityState.Modified,
        };

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Service(name, new FailOnceInterceptor(match)).CancelAsync(efp.Id, "bia", CancellationToken.None));
        var midway = await (await Read(name)).Context.MarketEfps.AsNoTracking().SingleAsync();
        Assert.NotEqual(MarketEfpStatus.Canceled, midway.Status);

        // Cancelamento iniciado: retomar não pode refazer o que o cancelamento desfez.
        await Assert.ThrowsAsync<BusinessException>(() => Service(name).ResumeAsync(efp.Id, "ana", CancellationToken.None));

        await Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None);
        await AssertCanceledCleanAsync(name, efp.Id);
    }

    [Fact]
    public async Task Cancel_Twice_ThrowsBusiness()
    {
        var name = await SeedEfpAsync();
        var efp = await CreateActiveAsync(name);
        await Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None);
        await Assert.ThrowsAsync<BusinessException>(() => Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_Concurrent_OneWinsOtherThrowsBusiness()
    {
        var name = await SeedEfpAsync();
        var efp = await CreateActiveAsync(name);
        var locks = new KeyedAsyncLock();
        var tasks = new[]
        {
            Task.Run(() => Service(name, null, locks).CancelAsync(efp.Id, "bia", CancellationToken.None)),
            Task.Run(() => Service(name, null, locks).CancelAsync(efp.Id, "bia", CancellationToken.None)),
        };
        try { await Task.WhenAll(tasks); } catch (BusinessException) { }

        Assert.Equal(1, tasks.Count(t => t.IsCompletedSuccessfully));
        Assert.IsType<BusinessException>(tasks.Single(t => t.IsFaulted).Exception!.InnerException);
        await AssertCanceledCleanAsync(name, efp.Id);
    }

    [Fact]
    public async Task Cancel_ConfirmedTrade_IsUnconfirmedAndDeleted()
    {
        var name = await SeedEfpAsync();
        var efp = await CreateActiveAsync(name);
        await TradeService(TestDb.CreateUnitOfWork(name))
            .SetConfirmedAsync(efp.FuturesTradeId!.Value, true, "ana", CancellationToken.None);

        await Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None);
        await AssertCanceledCleanAsync(name, efp.Id);
    }

    [Fact]
    public async Task Cancel_SettledTitle_RefusedAndLeavesEfpAsItWas()
    {
        var name = await SeedEfpAsync();
        var efp = await CreateActiveAsync(name);
        var c = (await Read(name)).Context;
        var doc = await c.FinancialDocuments.SingleAsync();
        doc.SettledAmount = 10m;
        await c.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<BusinessException>(() => Service(name).CancelAsync(efp.Id, "bia", CancellationToken.None));
        Assert.Contains("composição", e.Message);
        var r = (await Read(name)).Context;
        var after = await r.MarketEfps.AsNoTracking().SingleAsync();
        Assert.Equal(MarketEfpStatus.Active, after.Status);
        Assert.Null(after.CanceledBy);
        Assert.Equal(PriceFixationStatus.Confirmed, (await r.PurchaseContractsPriceFixations.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await r.MarketFuturesTrades.ToListAsync());
    }

    [Fact]
    public async Task Cancel_UnknownId_ThrowsKeyNotFound()
    {
        var name = await SeedEfpAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(name).CancelAsync(999, "bia", CancellationToken.None));
    }

    // ---------- Contratos para o value help ----------

    [Fact]
    public async Task ListContracts_ReturnsApprovedToBeFixedKgWithBalance()
    {
        var name = await SeedEfpAsync();
        var c = (await Read(name)).Context;
        var full = await c.PurchaseContracts.SingleAsync(x => x.Key == UsdPurchaseKey);
        full.FixedVolume = 1_000_000m;
        await c.SaveChangesAsync();

        var list = await Service(name).ListContractsAsync(MarketEfpContractSide.Purchase, null, CancellationToken.None);
        Assert.Equal(new string?[] { "CC000100", "CC000105" }, list.Select(x => x.Code).ToArray());
        Assert.Equal(1_000_000m, list[0].AvailableToPriceKg);
        Assert.Equal(CurrencyType.Brl, list[0].Currency);

        var sales = await Service(name).ListContractsAsync(MarketEfpContractSide.Sales, "cliente", CancellationToken.None);
        Assert.Equal("CV000200", Assert.Single(sales).Code);
        Assert.Empty(await Service(name).ListContractsAsync(MarketEfpContractSide.Purchase, "zzz", CancellationToken.None));
        Assert.Single(await Service(name).ListContractsAsync(MarketEfpContractSide.Purchase, "000105", CancellationToken.None));
    }
}
