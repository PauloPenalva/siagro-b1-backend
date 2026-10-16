using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// Testar a conexão de uma empresa: banco que não abre é falha, nunca "conectou com migrations pendentes".
/// </summary>
public class CompaniesTestConnectionServiceTests
{
    private const string Cs = "Server=db;Database=NAO_EXISTE;User Id=app;Password=x";

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyDatabaseProbe _probe = new();

    [Fact]
    public async Task Probe_that_cannot_connect_yields_failure_with_the_sql_error()
    {
        _probe.Result = new CompanyProbeResult(false, "Cannot open database \"NAO_EXISTE\"", [], "db", "NAO_EXISTE");

        var result = await new CompaniesTestConnectionService(_db, _probe).ExecuteAsync(null, Cs);

        Assert.False(result.Success);
        Assert.StartsWith("Falha ao conectar:", result.Message);
        Assert.Contains("Cannot open database", result.Message);
        Assert.Empty(result.PendingMigrations);
    }
}
