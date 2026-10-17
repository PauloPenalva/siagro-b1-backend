using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

public class CompanyCatalogTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private ServiceProvider BuildProvider(string? defaultCode = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("Company:Code", defaultCode)));
        services.AddDbContext<CommonDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddCompanyContext();
        return services.BuildServiceProvider();
    }

    private void Seed(ServiceProvider provider, params (string Code, string Name)[] companies)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
        foreach (var (code, name) in companies)
            db.Companies.Add(new Company { Code = code, CompanyName = name, ConnectionString = $"Server=.;Database={code}" });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetAll_projects_companies_ordered_by_code()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0002", "LM"), ("0001", "CEAGUI"));

        var all = await provider.GetRequiredService<ICompanyCatalog>().GetAllAsync();

        Assert.Equal(["0001", "0002"], all.Select(c => c.Code));
        Assert.Equal("Server=.;Database=0002", all[1].ConnectionString);
    }

    [Fact]
    public async Task GetByCode_miss_reloads_once_and_finds_a_company_registered_after_the_first_load()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0001", "CEAGUI"));
        var catalog = provider.GetRequiredService<ICompanyCatalog>();
        Assert.Empty((await catalog.GetAllAsync()).Where(c => c.Code == "0002"));

        Seed(provider, ("0002", "LM"));

        Assert.NotNull(await catalog.GetByCodeAsync("0002"));
        Assert.Null(await catalog.GetByCodeAsync("9999"));
    }

    [Fact]
    public async Task TryGetCached_only_reads_the_snapshot()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0001", "CEAGUI"));
        var catalog = provider.GetRequiredService<ICompanyCatalog>();

        Assert.Null(catalog.TryGetCached("0001"));      // nada carregado ainda
        await catalog.GetAllAsync();
        Assert.NotNull(catalog.TryGetCached("0001"));
        Assert.NotNull(catalog.TryGetCached("0001".ToLowerInvariant()));
    }

    [Fact]
    public async Task Invalidate_forces_the_next_read_to_hit_the_database()
    {
        using var provider = BuildProvider();
        Seed(provider, ("0001", "CEAGUI"));
        var catalog = provider.GetRequiredService<ICompanyCatalog>();
        await catalog.GetAllAsync();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
            var row = db.Companies.Single();
            row.CompanyName = "CEAGUI RENOMEADA";
            db.SaveChanges();
        }
        Assert.Equal("CEAGUI", (await catalog.GetAllAsync()).Single().CompanyName);   // ainda no cache

        catalog.Invalidate();

        Assert.Equal("CEAGUI RENOMEADA", (await catalog.GetAllAsync()).Single().CompanyName);
    }

    [Fact]
    public void Default_company_code_comes_from_configuration_or_0001()
    {
        using var provider = BuildProvider("0007");
        Assert.Equal("0007", provider.GetRequiredService<ICompanyCatalog>().DefaultCompanyCode);

        using var provider2 = BuildProvider();
        Assert.Equal("0001", provider2.GetRequiredService<ICompanyCatalog>().DefaultCompanyCode);
    }
}
