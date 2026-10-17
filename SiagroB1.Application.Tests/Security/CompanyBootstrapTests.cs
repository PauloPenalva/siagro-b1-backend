using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Startup;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// Deploys existentes têm COMPANIES vazia e perfis sem empresa. O bootstrap transforma a instalação
/// numa instalação de uma empresa sem passo manual, e é idempotente porque Web e Gateway o rodam.
/// </summary>
public class CompanyBootstrapTests
{
    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Microsoft.Extensions.Configuration.IConfiguration Config(string? code = null, string? name = null) =>
        TestConfiguration.With(
            ("ConnectionStrings:SiagroDB", "Server=.;Database=CEAGUI_SIAGRO_DEV"),
            ("CompanyName", "CEAGUI"),
            ("Company:Code", code),
            ("Company:Name", name));

    private Guid SeedUserWithProfile(string? companyCode)
    {
        var user = new User { Username = Guid.NewGuid().ToString("N"), FullName = "x" };
        _db.Users.Add(user);
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "P", CompanyCode = companyCode });
        _db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task Empty_catalog_registers_the_default_company_from_configuration_and_assigns_orphans()
    {
        var userId = SeedUserWithProfile(null);
        _db.UserSessions.Add(new UserSession { SessionId = "s1", UserId = userId, IsActive = true, ExpiresAt = DateTime.Now.AddHours(1) });
        _db.UserSessions.Add(new UserSession { SessionId = "s2", UserId = userId, IsActive = false, ExpiresAt = DateTime.Now.AddHours(-1) });
        _db.SaveChanges();

        var result = await CompanyBootstrap.RunAsync(_db, Config());

        Assert.True(result.CreatedDefault);
        var company = Assert.Single(_db.Companies);
        Assert.Equal("0001", company.Code);
        Assert.Equal("CEAGUI", company.CompanyName);
        Assert.Equal("Server=.;Database=CEAGUI_SIAGRO_DEV", company.ConnectionString);
        Assert.Equal("0001", _db.UserProfiles.Single().CompanyCode);
        Assert.Equal("0001", _db.UserSessions.Single(s => s.SessionId == "s1").CompanyCode);
        Assert.Null(_db.UserSessions.Single(s => s.SessionId == "s2").CompanyCode);   // inativa fica como está
        Assert.Equal(1, result.ProfilesAssigned);
        Assert.Equal(1, result.SessionsAssigned);
    }

    [Fact]
    public async Task Configured_code_and_name_are_used()
    {
        await CompanyBootstrap.RunAsync(_db, Config(code: "ceag", name: "CEAGUI COOPERATIVA"));

        var company = Assert.Single(_db.Companies);
        Assert.Equal("CEAG", company.Code);
        Assert.Equal("CEAGUI COOPERATIVA", company.CompanyName);
    }

    [Fact]
    public async Task Second_run_is_a_no_op()
    {
        await CompanyBootstrap.RunAsync(_db, Config());

        var result = await CompanyBootstrap.RunAsync(_db, Config());

        Assert.False(result.CreatedDefault);
        Assert.Single(_db.Companies);
        Assert.Equal(0, result.ProfilesAssigned);
    }

    [Fact]
    public async Task Non_empty_catalog_only_assigns_orphans_to_the_configured_code_when_it_exists()
    {
        _db.Companies.Add(new Company { Code = "0001", CompanyName = "A", ConnectionString = "a" });
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "B", ConnectionString = "b" });
        _db.SaveChanges();
        SeedUserWithProfile(null);
        SeedUserWithProfile("0002");

        var result = await CompanyBootstrap.RunAsync(_db, Config(code: "0002"));

        Assert.False(result.CreatedDefault);
        Assert.Equal(2, _db.Companies.Count());
        Assert.Equal(2, _db.UserProfiles.Count(p => p.CompanyCode == "0002"));
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Configured_code_missing_from_a_non_empty_catalog_falls_back_to_the_first_and_warns()
    {
        _db.Companies.Add(new Company { Code = "0005", CompanyName = "E", ConnectionString = "e" });
        _db.SaveChanges();
        SeedUserWithProfile(null);

        var result = await CompanyBootstrap.RunAsync(_db, Config(code: "0001"));

        Assert.Equal("0005", result.DefaultCode);
        Assert.NotNull(result.Warning);
        Assert.Equal("0005", _db.UserProfiles.Single().CompanyCode);
    }
}
