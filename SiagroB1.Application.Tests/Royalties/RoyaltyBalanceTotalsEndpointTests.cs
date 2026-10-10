using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.Functions.Royalties;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Royalties;

/// <summary>
/// Testes do ENDPOINT dos totais, não do <c>RoyaltyBalanceQuery.TotalsAsync</c> (esse tem os seus):
/// o que quebrou foi a fiação — o valor com que cada parâmetro da função OData chega à action. A
/// tela manda <c>'yyyy-MM-dd'</c> entre aspas e o token <c>null</c> sem aspas.
/// Host mínimo com o EDM real de <see cref="ODataModelBuilderExtensions"/> e o controller real.
/// </summary>
public class RoyaltyBalanceTotalsEndpointTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private const string AllNull =
        "FromDate=null,ToDate=null,CardCode=null,OriginCode=null,ItemCode=null,Situation=null,StorageAddressCode=null";

    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Development",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddScoped<IUnitOfWork>(_ => _db);

        var modelBuilder = new ODataConventionModelBuilder();
        modelBuilder.ConfigureODataEntities();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(RoyaltyBalanceTotalsController).Assembly)
            .AddOData(o =>
            {
                o.EnableQueryFeatures(null);
                o.AddRouteComponents("odata", modelBuilder.GetEdmModel());
            });

        var app = builder.Build();
        app.UseRouting();
        app.MapControllers();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private async Task SeedAsync()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed, TransactionDate = new DateTime(2026, 10, 10),
            NetWeight = 30_000m, RoyaltySituation = RoyaltySituation.Tested, RoyaltyOriginCode = "P0001",
        });
        await _db.SaveChangesAsync();
    }

    private static async Task<JsonElement[]> GetTotalsAsync(HttpClient client, string parameters)
    {
        var response = await client.GetAsync($"/odata/RoyaltyBalanceTotals({parameters})");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("value").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Bare_null_literals_mean_no_filter()
    {
        await SeedAsync();
        var (app, client) = await StartAsync();
        await using var _ = app;

        var total = Assert.Single(await GetTotalsAsync(client, AllNull));

        Assert.Equal("Tested", total.GetProperty("Situation").GetString());
        Assert.Equal(30_000m, total.GetProperty("Balance").GetDecimal());
    }

    [Fact]
    public async Task Quoted_dates_and_situation_filter_the_totals()
    {
        await SeedAsync();
        var (app, client) = await StartAsync();
        await using var _ = app;

        Assert.Single(await GetTotalsAsync(client,
            "FromDate='2026-10-01',ToDate='2026-10-10',CardCode='P0001',OriginCode=null,ItemCode='SOJA',Situation='Tested',StorageAddressCode=null"));
        Assert.Empty(await GetTotalsAsync(client,
            "FromDate='2026-10-11',ToDate=null,CardCode=null,OriginCode=null,ItemCode=null,Situation=null,StorageAddressCode=null"));
        Assert.Empty(await GetTotalsAsync(client,
            "FromDate=null,ToDate=null,CardCode=null,OriginCode=null,ItemCode=null,Situation='Declared',StorageAddressCode=null"));
    }
}
