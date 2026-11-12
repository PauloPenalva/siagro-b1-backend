using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Infra.Context;
using SapAgentService = SiagroB1.Application.Services.SAP.AgentService;
using SapBusinessPartnerService = SiagroB1.Application.Services.SAP.BusinessPartnerService;
using SapItemService = SiagroB1.Application.Services.SAP.ItemService;
using SapWarehouseService = SiagroB1.Application.Services.SAP.WarehouseService;

namespace SiagroB1.Application.Tests.MasterData;

/// <summary>
/// GAC-1196: o value help filtra <c>Inactive eq false</c> pelo OData, então a expressão tem de
/// virar SQL. O provider InMemory aceita LINQ que o SQL Server recusa; aqui o provider é o do
/// SQL Server e só se pede a tradução (<c>ToQueryString</c>) — nenhuma conexão é aberta.
/// </summary>
public class SapInactiveRecordsSqlTests
{
    private static SapErpDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<SapErpDbContext>()
            .UseSqlServer("Server=unused;Database=unused;TrustServerCertificate=True").Options);

    [Fact]
    public void Partner_filter_translates_to_sql()
    {
        var service = new SapBusinessPartnerService(SqlServerContext(), NullLogger<SapBusinessPartnerService>.Instance);

        var sql = service.QueryAll().Where(x => x.Inactive == false && x.CardType == "S")
            .OrderBy(x => x.CardName).Select(x => new { x.CardCode, x.CardName, x.Inactive }).ToQueryString();

        Assert.Contains("[frozenFor]", sql);
        Assert.Contains("[validFor]", sql);
    }

    [Fact]
    public void Warehouse_filter_translates_to_sql()
    {
        var service = new SapWarehouseService(
            SqlServerContext(), NullLogger<SapWarehouseService>.Instance, new ConfigurationBuilder().Build());

        var sql = service.QueryAll().Where(x => x.Inactive == false).OrderBy(x => x.Name).ToQueryString();

        Assert.Contains("[frozenFor]", sql);
        Assert.Contains("[QryGroup23]", sql);
    }

    [Fact]
    public void Item_filter_translates_to_sql()
    {
        var service = new SapItemService(
            SqlServerContext(), NullLogger<SapItemService>.Instance, new ConfigurationBuilder().Build());

        var sql = service.QueryAll().Where(x => x.Inactive == false).OrderBy(x => x.ItemName).ToQueryString();

        Assert.Contains("[frozenFor]", sql);
        Assert.Contains("[U_YKT_Integra_Siagro]", sql);
    }

    [Fact]
    public void Agent_filter_translates_to_sql()
    {
        var service = new SapAgentService(SqlServerContext(), NullLogger<SapAgentService>.Instance);

        var sql = service.QueryAll().Where(x => x.Inactive == "N").OrderBy(x => x.Code).ToQueryString();

        Assert.Contains("[Active]", sql);
        Assert.Contains("[Locked]", sql);
    }
}
