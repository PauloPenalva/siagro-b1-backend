using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// Cadastrar uma empresa é cadastrar um BANCO: a string tem de abrir, não pode ser a da COMMON e o
/// banco tem de estar com as migrations em dia — senão o próximo boot do Web recusaria subir.
/// </summary>
public class CompaniesCreateServiceTests
{
    private const string Cs = "Server=db;Database=LMCEREAIS_SIAGRO;User Id=app;Password=x";

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyDatabaseProbe _probe = new();
    private readonly FakeCompanyCatalog _catalog = new();

    private CompaniesCreateService Create() =>
        new(_db, _probe, _catalog,
            TestConfiguration.With(("ConnectionStrings:SiagroCommon", "Server=db;Database=CEAGUI_SIAGRO_COMMON;User Id=app;Password=x")),
            NullLogger<CompaniesCreateService>.Instance);

    private static Company Entity(string code = "0002", string cs = Cs) =>
        new() { Code = code, CompanyName = "LM CEREAIS", ConnectionString = cs };

    [Fact]
    public async Task Valid_company_is_saved_and_the_catalog_is_invalidated()
    {
        var saved = await Create().ExecuteAsync(Entity(code: "lm02"));

        Assert.Equal("LM02", saved.Code);
        Assert.Single(_db.Companies);
        Assert.Equal(1, _catalog.Invalidations);
        Assert.Equal([Cs], _probe.Probed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("A-01")]
    public async Task Code_must_be_1_to_4_letters_or_digits(string code)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity(code: code)));
        Assert.Contains("Código", ex.Message);
    }

    [Fact]
    public async Task Masked_connection_string_is_refused()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(Entity(cs: CompanyConnectionStrings.MaskPassword(Cs))));
        Assert.Contains("senha", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connection_string_pointing_to_the_common_database_is_refused()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(Entity(cs: "Server=db;Database=CEAGUI_SIAGRO_COMMON;User Id=app;Password=x")));
        Assert.Contains("COMMON", ex.Message);
    }

    [Fact]
    public async Task Unreachable_database_is_refused_with_the_probe_error()
    {
        _probe.Result = new(false, "Login failed for user 'app'.", [], "db", "LM");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity()));
        Assert.Contains("Login failed", ex.Message);
        Assert.Empty(_db.Companies);
    }

    [Fact]
    public async Task Database_with_pending_migrations_is_refused_with_the_command_to_run()
    {
        _probe.Result = new(true, null, ["20261016120000_X", "20261016130000_Y"], "db", "LMCEREAIS_SIAGRO");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity()));

        Assert.Contains("migrations pendentes", ex.Message);
        Assert.Contains("20261016120000_X", ex.Message);
        Assert.Contains("dotnet ef database update --context AppDbContext --connection", ex.Message);
        Assert.Empty(_db.Companies);
    }

    [Fact]
    public async Task Duplicate_code_is_refused()
    {
        await Create().ExecuteAsync(Entity());

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(Entity()));
        Assert.Contains("já existe", ex.Message);
    }
}
