using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Commodities, limites de exposição e câmbio fechado dos contratos (parte 4, Task 3).</summary>
public class MarketPositionSetupTests
{
    private static readonly FakeItemService Items = new(new() { ["SOJA"] = "SOJA EM GRAOS", ["MILHO"] = "MILHO" });

    private static MarketCommodityService Commodities(UnitOfWork db) =>
        new(db.Context, Items, new TestLogger<MarketCommodityService>());

    private static MarketCommodityItemService ItemsService(UnitOfWork db) =>
        new(db.Context, Items, new TestLogger<MarketCommodityItemService>());

    private static MarketCommodityMonthService MonthsService(UnitOfWork db) =>
        new(db.Context, new TestLogger<MarketCommodityMonthService>());

    private static MarketExposureLimitService Limits(UnitOfWork db, params string[] granted) =>
        new(db.Context, new MarketPermissionGuard(new FakeUserPermissions(granted)), new TestLogger<MarketExposureLimitService>());

    private static MarketContractFxLockService FxLock(UnitOfWork db, params string[] granted) =>
        new(db.Context, new MarketPermissionGuard(new FakeUserPermissions(granted)),
            new PurchaseContractsChangeLogService(db.Context), new SalesContractsChangeLogService(db.Context));

    private static MarketCommodity Soy(params string[] itemCodes) => new()
    {
        Code = "SOY", Name = "Soja", HedgeExchange = MarketExchange.Cbot,
        Items = itemCodes.Select(c => new MarketCommodityItem { CommodityCode = "SOY", ItemCode = c, ItemName = "NOME DO CLIENTE" }).ToList(),
    };

    [Fact]
    public void Edm_ExposesCommoditySetsWithStringKeyAndNavigations()
    {
        var builder = new Microsoft.OData.ModelBuilder.ODataConventionModelBuilder();
        SiagroB1.Web.ODataConfig.ODataConfigurations.ConfigureODataEntities(builder);
        var model = builder.GetEdmModel();

        var commodity = model.EntityContainer.FindEntitySet("MarketCommodities").EntityType;
        Assert.Equal(["Code"], commodity.DeclaredKey.Select(k => k.Name));
        Assert.Contains(commodity.NavigationProperties(), n => n.Name == "Items");
        Assert.Contains(commodity.NavigationProperties(), n => n.Name == "Months");
        Assert.NotNull(model.EntityContainer.FindEntitySet("MarketCommodityItems"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("MarketCommodityMonths"));
        var limits = model.EntityContainer.FindEntitySet("MarketExposureLimits").EntityType;
        Assert.Contains(limits.NavigationProperties(), n => n.Name == "Commodity");
    }

    // ---- rodada de correção 1 ----

    [Fact]
    public async Task DeepCreate_ClientIds_AreResetOnChildren()
    {
        var db = TestDb.CreateUnitOfWork();
        var payload = Soy("SOJA");
        payload.Items[0].Id = 99;
        payload.Months = [new() { Id = 77, CommodityCode = "SOY", DeliveryMonth = 6, FuturesMonth = 7 }];

        var created = await Commodities(db).CreateAsync(payload);

        Assert.NotEqual(99, created.Items.Single().Id);
        Assert.NotEqual(77, created.Months.Single().Id);
        var item = await ItemsService(db).CreateAsync(new MarketCommodityItem { Id = 55, CommodityCode = "SOY", ItemCode = "MILHO" });
        Assert.NotEqual(55, item.Id);
        var month = await MonthsService(db).CreateAsync(new MarketCommodityMonth { Id = 66, CommodityCode = "SOY", DeliveryMonth = 7, FuturesMonth = 7 });
        Assert.NotEqual(66, month.Id);
    }

    [Fact]
    public async Task Limit_NestedCommodityObject_IsIgnored()
    {
        var db = await SeedSoyAsync();
        var limit = Limit();
        limit.Commodity = new MarketCommodity { Code = "SOY", Name = "Duplicada", HedgeExchange = MarketExchange.B3 };

        var saved = await Limits(db, PermissionCodes.MarketBoardApprove).CreateAsync(limit, "diretor");

        Assert.Null(saved.Commodity);
        Assert.Equal("Soja", (await db.Context.MarketCommodities.AsNoTracking().SingleAsync()).Name);
    }

    private static Microsoft.AspNetCore.Mvc.ControllerContext UserContext() => new()
    {
        HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "ana")], "test")),
        },
    };

    [Fact]
    public async Task LimitsController_ManageOnly_Gets403OnPostPatchDelete_ButCommoditiesAcceptManage()
    {
        var db = await SeedSoyAsync();
        var manage = new MarketPermissionGuard(new FakeUserPermissions(PermissionCodes.MarketManage));
        var limits = new SiagroB1.Web.Controllers.MarketExposureLimitsController(Limits(db, PermissionCodes.MarketManage), manage)
        {
            ControllerContext = UserContext(),
        };

        static void Assert403(Microsoft.AspNetCore.Mvc.IActionResult r) =>
            Assert.Equal(403, Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(r).StatusCode);
        Assert403(await limits.Post(Limit()));
        Assert403(await limits.Patch(1, new Microsoft.AspNetCore.OData.Deltas.Delta<MarketExposureLimit>()));
        Assert403(await limits.Delete(1));
        Assert.Empty(db.Context.MarketExposureLimits);

        var commodities = new SiagroB1.Web.Controllers.MarketCommoditiesController(Commodities(db), manage)
        {
            ControllerContext = UserContext(),
        };
        var result = await commodities.Post(new MarketCommodity { Code = "CORN", Name = "Milho", HedgeExchange = MarketExchange.Cbot });
        Assert.IsType<Microsoft.AspNetCore.OData.Results.CreatedODataResult<MarketCommodity>>(result);
    }

    /// <summary>
    /// O código da commodity dos filhos vem da rota ou do cabeçalho: o "obrigatório" implícito do campo não pode
    /// recusar o POST em profundidade nem o POST aninhado (visto ao vivo: 400 "The CommodityCode field is required").
    /// </summary>
    [Fact]
    public async Task CommodityControllers_ChildCommodityCodeIsServerFilled_NotRequiredFromClient()
    {
        var db = TestDb.CreateUnitOfWork();
        var manage = new MarketPermissionGuard(new FakeUserPermissions(PermissionCodes.MarketManage));
        var commodities = new SiagroB1.Web.Controllers.MarketCommoditiesController(Commodities(db), manage)
        {
            ControllerContext = UserContext(),
        };
        commodities.ModelState.AddModelError("Months[0].CommodityCode", "The CommodityCode field is required.");
        var created = await commodities.Post(new MarketCommodity
        {
            Code = "SOY", Name = "Soja", HedgeExchange = MarketExchange.Cbot,
            Months = [new MarketCommodityMonth { CommodityCode = null!, DeliveryMonth = 6, FuturesMonth = 7, YearOffset = 0 }],
        });
        Assert.IsType<Microsoft.AspNetCore.OData.Results.CreatedODataResult<MarketCommodity>>(created);
        Assert.Equal("SOY", Assert.Single(db.Context.MarketCommodityMonths).CommodityCode);

        var months = new SiagroB1.Web.Controllers.MarketCommodityMonthsController(
            new MarketCommodityMonthService(db.Context, Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketCommodityMonthService>.Instance), manage)
        {
            ControllerContext = UserContext(),
        };
        months.ModelState.AddModelError("commodityCode", "The CommodityCode field is required.");
        var nested = await months.PostNested("'SOY'", new MarketCommodityMonth
        {
            CommodityCode = null!, DeliveryMonth = 7, FuturesMonth = 7, YearOffset = 0,
        });
        Assert.IsType<Microsoft.AspNetCore.OData.Results.CreatedODataResult<MarketCommodityMonth>>(nested);
        Assert.Equal(2, db.Context.MarketCommodityMonths.Count());

        // Erro de outro campo continua reprovando.
        months.ModelState.AddModelError("DeliveryMonth", "inválido");
        var invalid = await months.PostNested("'SOY'", new MarketCommodityMonth
        {
            CommodityCode = null!, DeliveryMonth = 8, FuturesMonth = 8, YearOffset = 0,
        });
        Assert.Equal(400, Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(invalid).StatusCode);
    }

    [Fact]
    public async Task SetupController_MalformedBody_Returns400PlainText()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = new SiagroB1.Web.Controllers.MarketPositionSetupController(
            Commodities(db), FxLock(db, PermissionCodes.MarketManage),
            new MarketPermissionGuard(new FakeUserPermissions(PermissionCodes.MarketManage)),
            new TestLogger<SiagroB1.Web.Controllers.MarketPositionSetupController>())
        {
            ControllerContext = UserContext(),
        };
        controller.ModelState.AddModelError("", "json inválido");

        var result = await controller.SetFxLock("purchase", Guid.NewGuid(), null, CancellationToken.None);

        var content = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(result);
        Assert.Equal(400, content.StatusCode);
        Assert.StartsWith("text/plain", content.ContentType);
    }

    // ---- commodities ----

    [Fact]
    public async Task Samples_AreIdempotent_AndSoyDeliverySixMapsToJuly()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = Commodities(db);

        Assert.Equal(2, await service.CreateSamplesAsync(CancellationToken.None));
        Assert.Equal(0, await service.CreateSamplesAsync(CancellationToken.None));

        var soy = await db.Context.MarketCommodities.Include(c => c.Months).Include(c => c.Items).SingleAsync(c => c.Code == "SOY");
        Assert.Equal("Soja", soy.Name);
        Assert.Equal(MarketExchange.Cbot, soy.HedgeExchange);
        Assert.Empty(soy.Items);
        Assert.Equal(12, soy.Months.Count);
        var june = soy.Months.Single(m => m.DeliveryMonth == 6);
        Assert.Equal((7, 0), (june.FuturesMonth, june.YearOffset));
        var december = soy.Months.Single(m => m.DeliveryMonth == 12);
        Assert.Equal((1, 1), (december.FuturesMonth, december.YearOffset));
        var corn = await db.Context.MarketCommodities.Include(c => c.Months).SingleAsync(c => c.Code == "CORN");
        Assert.Equal(12, corn.Months.Single(m => m.DeliveryMonth == 12).FuturesMonth);
    }

    [Fact]
    public async Task Samples_CreateOnlyWhatIsMissing()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.MarketCommodities.Add(new MarketCommodity { Code = "SOY", Name = "Minha soja", HedgeExchange = MarketExchange.B3 });
        await db.SaveChangesAsync();

        Assert.Equal(1, await Commodities(db).CreateSamplesAsync(CancellationToken.None));
        Assert.Equal("Minha soja", (await db.Context.MarketCommodities.SingleAsync(c => c.Code == "SOY")).Name);
    }

    [Fact]
    public async Task DeepCreate_FillsItemNameFromMaster_IgnoringClientValue()
    {
        var db = TestDb.CreateUnitOfWork();

        var created = await Commodities(db).CreateAsync(Soy("SOJA"));

        Assert.Equal("SOJA EM GRAOS", created.Items.Single().ItemName);
        Assert.Equal("SOJA EM GRAOS", (await db.Context.MarketCommodityItems.AsNoTracking().SingleAsync()).ItemName);
    }

    [Fact]
    public async Task Item_UnknownProduct_Rejected()
    {
        var db = TestDb.CreateUnitOfWork();

        var e = await Assert.ThrowsAsync<BusinessException>(() => Commodities(db).CreateAsync(Soy("XYZ")));
        Assert.Contains("não encontrado", e.Message);
        Assert.Empty(db.Context.MarketCommodities);
    }

    [Fact]
    public async Task Item_AlreadyInAnotherCommodity_RejectedNamingIt()
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateAsync(Soy("SOJA"));
        await Commodities(db).CreateAsync(new MarketCommodity { Code = "CORN", Name = "Milho", HedgeExchange = MarketExchange.Cbot });
        db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<BusinessException>(() => ItemsService(db).CreateAsync(
            new MarketCommodityItem { CommodityCode = "CORN", ItemCode = "SOJA" }));
        Assert.Contains("SOY", e.Message);

        var deep = new MarketCommodity
        {
            Code = "OUTRA", Name = "Outra", HedgeExchange = MarketExchange.Cbot,
            Items = [new MarketCommodityItem { CommodityCode = "OUTRA", ItemCode = "SOJA" }],
        };
        e = await Assert.ThrowsAsync<BusinessException>(() => Commodities(db).CreateAsync(deep));
        Assert.Contains("SOY", e.Message);
    }

    [Fact]
    public async Task Item_AddedAlone_NameComesFromMaster()
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateAsync(Soy());

        var item = await ItemsService(db).CreateAsync(new MarketCommodityItem { CommodityCode = "SOY", ItemCode = " MILHO ", ItemName = "x" });

        Assert.Equal("MILHO", item.ItemCode);
        Assert.Equal("MILHO", item.ItemName);
    }

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(13, 5, 0)]
    [InlineData(6, 0, 0)]
    [InlineData(6, 13, 0)]
    [InlineData(6, 7, 2)]
    [InlineData(6, 7, -1)]
    public async Task Month_OutOfRange_Rejected(int delivery, int futures, int offset)
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateAsync(Soy());

        await Assert.ThrowsAsync<BusinessException>(() => MonthsService(db).CreateAsync(
            new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = delivery, FuturesMonth = futures, YearOffset = offset }));
    }

    [Fact]
    public async Task Month_DuplicateDeliveryMonth_Rejected_AlsoInDeepCreate()
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateAsync(Soy());
        await MonthsService(db).CreateAsync(new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = 6, FuturesMonth = 7 });

        var e = await Assert.ThrowsAsync<BusinessException>(() => MonthsService(db).CreateAsync(
            new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = 6, FuturesMonth = 8 }));
        Assert.Contains("mês de entrega 6", e.Message);

        var deep = new MarketCommodity
        {
            Code = "CORN", Name = "Milho", HedgeExchange = MarketExchange.Cbot,
            Months =
            [
                new() { CommodityCode = "CORN", DeliveryMonth = 3, FuturesMonth = 3 },
                new() { CommodityCode = "CORN", DeliveryMonth = 3, FuturesMonth = 5 },
            ],
        };
        await Assert.ThrowsAsync<BusinessException>(() => Commodities(db).CreateAsync(deep));
        Assert.False(await db.Context.MarketCommodities.AnyAsync(c => c.Code == "CORN"));
    }

    [Fact]
    public async Task Commodity_DuplicateCodeIgnoringCase_AndBlankFields_Rejected()
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateAsync(Soy());

        await Assert.ThrowsAsync<BusinessException>(() => Commodities(db).CreateAsync(
            new MarketCommodity { Code = "soy", Name = "Outra", HedgeExchange = MarketExchange.Cbot }));
        await Assert.ThrowsAsync<BusinessException>(() => Commodities(db).CreateAsync(
            new MarketCommodity { Code = " ", Name = "Outra", HedgeExchange = MarketExchange.Cbot }));
        await Assert.ThrowsAsync<BusinessException>(() => Commodities(db).CreateAsync(
            new MarketCommodity { Code = "A/B", Name = "Outra", HedgeExchange = MarketExchange.Cbot }));
    }

    [Fact]
    public async Task Commodity_Update_IgnoresPayloadChildren_AndDeleteBlockedByLimits()
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateAsync(Soy("SOJA"));
        db.Context.ChangeTracker.Clear();

        var payload = Soy("MILHO");
        payload.Name = "Soja em grão";
        await Commodities(db).UpdateAsync("SOY", payload);
        db.Context.ChangeTracker.Clear();
        Assert.Equal("Soja em grão", (await db.Context.MarketCommodities.SingleAsync()).Name);
        Assert.Equal("SOJA", (await db.Context.MarketCommodityItems.SingleAsync()).ItemCode);

        await Limits(db, PermissionCodes.MarketBoardApprove).CreateAsync(
            new MarketExposureLimit { Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", MaxLong = 100 }, "diretor");
        await Assert.ThrowsAsync<BusinessException>(() => Commodities(db).DeleteAsync("SOY"));
    }

    [Fact]
    public async Task Commodity_Delete_RemovesChildren()
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateSamplesAsync(CancellationToken.None);
        db.Context.ChangeTracker.Clear();
        await Commodities(db).CreateAsync(new MarketCommodity
        {
            Code = "WHEAT", Name = "Trigo", HedgeExchange = MarketExchange.Cbot,
            Items = [new MarketCommodityItem { CommodityCode = "WHEAT", ItemCode = "SOJA" }],
        });
        db.Context.ChangeTracker.Clear();

        Assert.False(await Commodities(db).DeleteAsync("NOPE"));
        Assert.True(await Commodities(db).DeleteAsync("WHEAT"));
        Assert.Empty(db.Context.MarketCommodityItems);
        Assert.True(await Commodities(db).DeleteAsync("SOY"));
        Assert.DoesNotContain(db.Context.MarketCommodityMonths, m => m.CommodityCode == "SOY");
    }

    // ---- limites ----

    private static MarketExposureLimit Limit(MarketLimitScope scope = MarketLimitScope.Commodity, string? commodity = "SOY",
        int? year = null, int? month = null, decimal? maxLong = 1000, decimal? maxShort = null) => new()
    {
        Scope = scope, CommodityCode = commodity, Year = year, Month = month, MaxLong = maxLong, MaxShort = maxShort,
    };

    private static async Task<UnitOfWork> SeedSoyAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        await Commodities(db).CreateAsync(Soy());
        db.Context.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Limit_WithoutBoardPermission_Forbidden_EvenWithManage()
    {
        var db = await SeedSoyAsync();
        var service = Limits(db, PermissionCodes.MarketManage);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(Limit(), "gerente"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(1, Limit(), "gerente"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(1, "gerente"));
        Assert.Empty(db.Context.MarketExposureLimits);
    }

    [Fact]
    public async Task Limit_Create_StampsUserAndTime_AndRoundsToScale()
    {
        var db = await SeedSoyAsync();
        var limit = Limit(maxLong: 1000.004m, maxShort: 500);
        limit.UpdatedBy = "cliente";

        var saved = await Limits(db, PermissionCodes.MarketBoardApprove).CreateAsync(limit, "diretor");

        Assert.Equal("diretor", saved.UpdatedBy);
        Assert.NotNull(saved.UpdatedAt);
        Assert.Equal(1000m, saved.MaxLong);
    }

    [Theory]
    [InlineData(MarketLimitScope.Commodity, null, null, null, 100.0, null)]
    [InlineData(MarketLimitScope.Commodity, "NOPE", null, null, 100.0, null)]
    [InlineData(MarketLimitScope.Usd, "SOY", null, null, 100.0, null)]
    [InlineData(MarketLimitScope.Commodity, "SOY", 2027, null, 100.0, null)]
    [InlineData(MarketLimitScope.Commodity, "SOY", null, 3, 100.0, null)]
    [InlineData(MarketLimitScope.Commodity, "SOY", 2027, 13, 100.0, null)]
    [InlineData(MarketLimitScope.Commodity, "SOY", null, null, null, null)]
    [InlineData(MarketLimitScope.Commodity, "SOY", null, null, 0.0, null)]
    [InlineData(MarketLimitScope.Commodity, "SOY", null, null, 100.0, -5.0)]
    [InlineData(MarketLimitScope.Commodity, "SOY", null, null, 0.004, null)]
    public async Task Limit_InvalidShapes_Rejected(MarketLimitScope scope, string? commodity, int? year, int? month,
        double? maxLong, double? maxShort)
    {
        var db = await SeedSoyAsync();
        var limit = Limit(scope, commodity, year, month, (decimal?)maxLong, (decimal?)maxShort);

        await Assert.ThrowsAsync<BusinessException>(() =>
            Limits(db, PermissionCodes.MarketBoardApprove).CreateAsync(limit, "diretor"));
    }

    [Fact]
    public async Task Limit_UsdWithoutCommodity_AndMonthlyOnly_Accepted()
    {
        var db = await SeedSoyAsync();
        var service = Limits(db, PermissionCodes.MarketBoardApprove);

        await service.CreateAsync(Limit(MarketLimitScope.Usd, null, maxLong: 1_000_000, maxShort: 2_000_000), "diretor");
        await service.CreateAsync(Limit(year: 2027, month: 3), "diretor");

        Assert.Equal(2, await db.Context.MarketExposureLimits.CountAsync());
    }

    [Fact]
    public async Task Limit_DuplicateActiveRejected_InactiveAndOtherPeriodAllowed()
    {
        var db = await SeedSoyAsync();
        var service = Limits(db, PermissionCodes.MarketBoardApprove);
        await service.CreateAsync(Limit(), "diretor");

        var e = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Limit(maxLong: 5), "diretor"));
        Assert.Contains("limite ativo", e.Message);
        var inactive = Limit();
        inactive.Active = false;
        await service.CreateAsync(inactive, "diretor");
        await service.CreateAsync(Limit(year: 2027, month: 3), "diretor");
        await service.CreateAsync(Limit(MarketLimitScope.Usd, null), "diretor");

        // Reativar o inativo colidiria com o ativo.
        db.Context.ChangeTracker.Clear();
        var again = Limit();
        again.Active = true;
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(2, again, "diretor"));
        // O próprio limite pode ser salvo sem colidir consigo mesmo.
        db.Context.ChangeTracker.Clear();
        Assert.NotNull(await service.UpdateAsync(1, Limit(maxLong: 2000), "diretor"));
    }

    [Fact]
    public async Task Limit_Delete_RemovesBreaches()
    {
        var db = await SeedSoyAsync();
        var service = Limits(db, PermissionCodes.MarketBoardApprove);
        var limit = await service.CreateAsync(Limit(), "diretor");
        db.Context.MarketLimitBreaches.Add(new MarketLimitBreach
        {
            LimitId = limit.Id, Date = new DateTime(2026, 11, 6), Value = 1, LimitValue = 1, DetectedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        Assert.True(await service.DeleteAsync(limit.Id, "diretor"));
        Assert.Empty(db.Context.MarketLimitBreaches);
        Assert.False(await service.DeleteAsync(limit.Id, "diretor"));
    }

    // ---- câmbio fechado ----

    private static async Task<(UnitOfWork Db, Guid Purchase, Guid Sales)> SeedContractsAsync(
        ContractStatus status = ContractStatus.Approved)
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F1", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "26/27", DeliveryLocationCode = "01", Status = status, MarketFxRate = 5.1m,
        };
        var sales = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-1", CardCode = "C1", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "26/27", Status = status,
        };
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.SalesContracts.Add(sales);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, purchase.Key, sales.Key);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.InApproval)]
    [InlineData(ContractStatus.Canceled)]
    public async Task FxLock_NotApproved_Rejected(ContractStatus status)
    {
        var (db, purchase, sales) = await SeedContractsAsync(status);
        var service = FxLock(db, PermissionCodes.MarketManage);

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SetAsync(MarketEfpContractSide.Purchase, purchase, true, 5.4m, "gerente"));
        Assert.Contains("aprovado", e.Message);
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.SetAsync(MarketEfpContractSide.Sales, sales, true, null, "gerente"));
    }

    [Fact]
    public async Task FxLock_Purchase_LocksWithRate_AndUnlockKeepsRate_AndLogs()
    {
        var (db, purchase, _) = await SeedContractsAsync();
        var service = FxLock(db, PermissionCodes.MarketManage);

        await service.SetAsync(MarketEfpContractSide.Purchase, purchase, true, 5.4m, "gerente");
        var locked = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.True(locked.MarketFxLocked);
        Assert.Equal(5.4m, locked.MarketFxRate);

        await service.SetAsync(MarketEfpContractSide.Purchase, purchase, false, null, "gerente");
        var open = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.False(open.MarketFxLocked);
        Assert.Equal(5.4m, open.MarketFxRate);

        var log = await db.Context.PurchaseContractsChangeLogs.AsNoTracking().ToListAsync();
        Assert.Equivalent(new[] { "Câmbio fechado a 5,4000", "Câmbio reaberto" }, log.Select(l => l.NewValue).ToArray());
        Assert.All(log, l => Assert.Equal("gerente", l.ChangedBy));
    }

    [Fact]
    public async Task FxLock_Sales_LocksWithoutRate_AndLogs_OtherColumnsUntouched()
    {
        var (db, _, sales) = await SeedContractsAsync();

        await FxLock(db, PermissionCodes.MarketManage).SetAsync(MarketEfpContractSide.Sales, sales, true, null, "gerente");

        var contract = await db.Context.SalesContracts.AsNoTracking().SingleAsync();
        Assert.True(contract.MarketFxLocked);
        Assert.Null(contract.MarketFxRate);
        Assert.Equal("SC-1", contract.Code);
        Assert.Equal("Câmbio fechado", (await db.Context.SalesContractsChangeLogs.AsNoTracking().SingleAsync()).NewValue);
    }

    [Fact]
    public async Task FxLock_Guards_PermissionRateAndExistence()
    {
        var (db, purchase, _) = await SeedContractsAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            FxLock(db).SetAsync(MarketEfpContractSide.Purchase, purchase, true, 5m, "x"));
        var service = FxLock(db, PermissionCodes.MarketManage);
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.SetAsync(MarketEfpContractSide.Purchase, purchase, true, 0m, "x"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SetAsync(MarketEfpContractSide.Sales, Guid.NewGuid(), true, 5m, "x"));
        Assert.False((await db.Context.PurchaseContracts.AsNoTracking().SingleAsync()).MarketFxLocked);
    }
}
