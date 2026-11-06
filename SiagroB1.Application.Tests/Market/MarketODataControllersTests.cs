using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Pricing;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Infra;
using SiagroB1.Web.Base;
using SiagroB1.Web.Controllers;
using SiagroB1.Web.Filters;
using static SiagroB1.Application.Tests.Market.MarketPricingTestHelpers;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Controllers do Mercado: GET por chave como consulta ($expand no banco), deep create com linhas, erros de gravação.</summary>
public class MarketODataControllersTests
{
    private static ControllerContext UserContext() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ana")], "test")),
        },
    };

    private static MarketPriceTablesController TablesController(UnitOfWork db) =>
        new(TableService(db), new MarketPermissionGuard(new FakeUserPermissions(PermissionCodes.MarketManage)))
        {
            ControllerContext = UserContext(),
        };

    [Fact]
    public async Task PriceTables_GetByKey_ReturnsSingleResultQuery_And404WhenMissing()
    {
        var db = await SeedPricingAsync(Brl());
        await LineService(db).CreateAsync(Line("CCMF27"), "ana");

        var result = await TablesController(db).Get(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var single = Assert.IsType<SingleResult<MarketPriceTable>>(ok.Value);
        Assert.Equal(1, Assert.Single(single.Queryable.ToList()).Id);
        Assert.IsType<NotFoundResult>((await TablesController(db).Get(999)).Result);
    }

    [Fact]
    public async Task PriceTables_DeepCreate_ResponseCarriesCreatedLines()
    {
        var db = await SeedPricingAsync(Brl());
        var table = new MarketPriceTable
        {
            Description = "Nova", ItemCode = "SOJA", ItemName = "", HarvestSeasonCode = "26/27", Lines = [Line("CCMF27")],
        };

        var result = await TablesController(db).Post(table);

        var created = Assert.IsType<CreatedODataResult<MarketPriceTable>>(result);
        var line = Assert.Single(created.Entity.Lines);
        Assert.True(line.Id > 0);
        Assert.Equal(created.Entity.Id, line.PriceTableId);
    }

    [Fact]
    public async Task Instruments_GetByKey_FiltersByCode()
    {
        var db = await SeedPricingAsync(Brl(), SpotFx());
        var controller = new MarketInstrumentsController(MarketTestHelpers.InstrumentService(db),
            new MarketPermissionGuard(new FakeUserPermissions())) { ControllerContext = UserContext() };

        var ok = Assert.IsType<OkObjectResult>((await controller.Get("DOL%20COM")).Result);

        Assert.Equal("DOL COM", Assert.Single(Assert.IsType<SingleResult<MarketInstrument>>(ok.Value).Queryable.ToList()).Code);
    }

    [Fact]
    public async Task Tickets_GetByKey_ReturnsSingleResultQuery()
    {
        var db = await MarketTradeTicketTestHelpers.SeedTicketScenarioAsync();
        var ticket = await MarketTradeTicketTestHelpers.TicketService(db).CreateAsync(
            MarketTradeTicketTestHelpers.Input(), "comprador", MarketTradeTicketTestHelpers.Today, CancellationToken.None);
        var controller = new MarketTradeTicketsController(db.Context, MarketTradeTicketTestHelpers.TicketService(db),
            TimeProvider.System, NullLogger<MarketTradeTicketsController>.Instance) { ControllerContext = UserContext() };

        var ok = Assert.IsType<OkObjectResult>(await controller.Get(ticket.Id));

        Assert.Equal(ticket.Id, Assert.Single(Assert.IsType<SingleResult<MarketTradeTicket>>(ok.Value).Queryable.ToList()).Id);
        Assert.IsType<NotFoundResult>(await controller.Get(999));
    }

    [Fact]
    public void DescribeSaveError_MapsOverflowAndGenericDbErrors()
    {
        Assert.Equal(MarketODataController<MarketDestination, int>.OutOfRangeMessage,
            MarketODataController<MarketDestination, int>.DescribeSaveError(
                new DbUpdateException("x", new ArgumentException("Parameter value '1E+20' is out of range."))));
        Assert.Equal(MarketODataController<MarketDestination, int>.SaveFailedMessage,
            MarketODataController<MarketDestination, int>.DescribeSaveError(new DbUpdateException("x")));
        Assert.Null(MarketODataController<MarketDestination, int>.DescribeSaveError(new InvalidOperationException()));
    }

    [Fact]
    public async Task Destinations_SaveError_Becomes400PtBr()
    {
        var name = Guid.NewGuid().ToString();
        await SeedPricingAsync(name);
        var db = TestDb.CreateUnitOfWork(name, new ThrowOnSaveInterceptor(e => e.Entity is MarketDestination));
        var controller = new MarketDestinationsController(DestinationService(db),
            new MarketPermissionGuard(new FakeUserPermissions(PermissionCodes.MarketManage))) { ControllerContext = UserContext() };

        var result = await controller.Post(new MarketDestination { Name = "Porto Novo" });

        var bad = Assert.IsType<BadRequestODataResult>(result);
        Assert.Equal(MarketODataController<MarketDestination, int>.SaveFailedMessage, bad.Error.Message);
    }

    [Fact]
    public void PriceGrid_WithConnection_CopiesCmaStatus()
    {
        var grid = new MarketPriceGrid("26/27", "4104808", DateTime.UtcNow, []);

        var result = MarketPriceGridController.WithConnection(grid,
            new LiveQuoteSnapshot(3, CmaConnectionStatus.Error, "Sem CMA", null));

        Assert.Equal(CmaConnectionStatus.Error, result.ConnectionStatus);
        Assert.Equal("Sem CMA", result.ConnectionMessage);
    }

    [Fact]
    public void EveryMarketController_HasModuleEnabledFilter()
    {
        var controllers = typeof(MarketPriceGridController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.Name.StartsWith("Market", StringComparison.Ordinal)
                        && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

        Assert.True(controllers.Count >= 9);
        Assert.All(controllers, t => Assert.True(
            t.GetCustomAttribute<MarketModuleEnabledAttribute>(inherit: true) is not null, t.Name + " sem [MarketModuleEnabled]"));
    }
}
