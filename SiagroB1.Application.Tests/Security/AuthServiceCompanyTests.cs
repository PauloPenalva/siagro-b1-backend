using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// Login como no SAP B1: escolhe-se a empresa, depois usuário e senha. A sessão grava a empresa, e
/// permissões/papéis da resposta são só os da empresa escolhida.
/// </summary>
public class AuthServiceCompanyTests
{
    private static readonly CompanyInfo A = new("0001", "CEAGUI", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "LM CEREAIS", "Server=.;Database=B");

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyContext _companyContext = FakeCompanyContext.Unresolved();

    private AuthService CreateService(params CompanyInfo[] companies) =>
        new(_db,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new FakeSapUserProvisioner(),
            NullLogger<AuthService>.Instance,
            new FakeCompanyCatalog(companies),
            _companyContext);

    private User SeedUser(string username, bool isAdmin = false, params string[] profileCompanies)
    {
        var user = new User { Username = username, FullName = username, IsAdmin = isAdmin, PasswordHash = PasswordHasher.Hash("senha") };
        _db.Users.Add(user);
        if (!_db.Profiles.Any(p => p.Code == "OPER"))
        {
            _db.Profiles.Add(new Profile { Code = "OPER", Description = "Operador" });
            _db.Roles.Add(new Role { Code = "OPERADOR" });
            _db.ProfileRoles.Add(new ProfileRole { ProfileCode = "OPER", RoleCode = "OPERADOR" });
            _db.Permissions.Add(new Permission { Code = "WEIGHING_MANUAL_ENTRY", Description = "x" });
            _db.RolesPermissions.Add(new RolePermission { RoleCode = "OPERADOR", PermissionCode = "WEIGHING_MANUAL_ENTRY" });
        }
        foreach (var company in profileCompanies)
            _db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "OPER", CompanyCode = company });
        _db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task User_without_profile_in_the_company_is_refused_with_the_company_name()
    {
        SeedUser("joao", profileCompanies: ["0001"]);

        var result = await CreateService(A, B).LoginAsync("joao", "senha", "0002");

        Assert.False(result.Success);
        Assert.Equal("Usuário sem acesso à empresa LM CEREAIS.", result.Message);
        Assert.Empty(_db.UserSessions);
    }

    [Fact]
    public async Task Successful_login_stores_the_company_in_the_session_and_scopes_permissions()
    {
        SeedUser("joao", profileCompanies: ["0002"]);

        var result = await CreateService(A, B).LoginAsync("joao", "senha", "0002");

        Assert.True(result.Success, result.Message);
        Assert.Equal("0002", _db.UserSessions.Single().CompanyCode);
        Assert.Equal("0002", result.User!.CompanyCode);
        Assert.Equal("LM CEREAIS", result.User.CompanyName);
        Assert.Equal(["WEIGHING_MANUAL_ENTRY"], result.User.Permissions);
        Assert.Equal(["OPERADOR"], result.User.Roles);
        Assert.Equal("0002", _companyContext.Current?.Code);
        Assert.Contains(result.Claims!, c => c.Type == CompanyClaims.CompanyCode && c.Value == "0002");
    }

    [Fact]
    public async Task Permissions_of_another_company_do_not_come_back()
    {
        SeedUser("joao", profileCompanies: ["0001", "0002"]);
        _db.Roles.Add(new Role { Code = "SUPERVISOR" });
        _db.Profiles.Add(new Profile { Code = "SUP", Description = "Sup" });
        _db.ProfileRoles.Add(new ProfileRole { ProfileCode = "SUP", RoleCode = "SUPERVISOR" });
        _db.UserProfiles.Add(new UserProfile { UserId = _db.Users.Single().Id, ProfileCode = "SUP", CompanyCode = "0001" });
        _db.SaveChanges();

        var result = await CreateService(A, B).LoginAsync("joao", "senha", "0002");

        Assert.DoesNotContain("SUPERVISOR", result.User!.Roles);
    }

    [Fact]
    public async Task Admin_logs_into_any_company()
    {
        SeedUser("admin", isAdmin: true);

        var result = await CreateService(A, B).LoginAsync("admin", "senha", "0002");

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task Login_without_company_code_and_single_company_selects_it()
    {
        SeedUser("joao", profileCompanies: ["0001"]);

        var result = await CreateService(A).LoginAsync("joao", "senha", null);

        Assert.True(result.Success, result.Message);
        Assert.Equal("0001", _db.UserSessions.Single().CompanyCode);
    }

    [Fact]
    public async Task Login_without_company_code_and_several_companies_asks_for_one()
    {
        SeedUser("joao", profileCompanies: ["0001"]);

        var result = await CreateService(A, B).LoginAsync("joao", "senha", null);

        Assert.False(result.Success);
        Assert.Equal("Selecione a empresa.", result.Message);
    }

    [Fact]
    public async Task Unknown_company_code_is_refused_before_checking_credentials()
    {
        var result = await CreateService(A, B).LoginAsync("ninguem", "x", "9999");

        Assert.False(result.Success);
        Assert.Equal("Empresa inválida.", result.Message);
    }

    [Fact]
    public async Task Wrong_password_keeps_the_generic_message()
    {
        SeedUser("joao", profileCompanies: ["0001"]);

        var result = await CreateService(A).LoginAsync("joao", "errada", "0001");

        Assert.Equal("Usuário ou Senha inválidos", result.Message);
    }

    [Fact]
    public async Task ListCompanies_exposes_only_code_and_name_ordered_by_name()
    {
        var list = await CreateService(B, A).ListCompaniesAsync();

        Assert.Equal(["CEAGUI", "LM CEREAIS"], list.Select(c => c.CompanyName));
        Assert.Equal(["0001", "0002"], list.Select(c => c.Code));
    }

    [Fact]
    public async Task GetUserInfo_uses_the_company_of_the_request_scope()
    {
        SeedUser("joao", profileCompanies: ["0001", "0002"]);
        _companyContext.Set(B);

        var info = await CreateService(A, B).GetUserInfoAsync("joao");

        Assert.Equal("0002", info!.CompanyCode);
        Assert.Equal("LM CEREAIS", info.CompanyName);
    }
}
