using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Middlewares;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// O middleware é quem transforma o cookie de sessão em identidade nos três hosts; agora também
/// publica a EMPRESA da sessão no escopo da requisição — e tem de ser no escopo da requisição
/// (RequestServices), não no escopo interno que ele abre para ler a COMMON.
/// </summary>
public class CookieAuthMiddlewareCompanyTests
{
    private static readonly CompanyInfo A = new("0001", "CEAGUI", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "LM CEREAIS", "Server=.;Database=B");

    private readonly string _dbName = Guid.NewGuid().ToString();

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK")));
        services.AddDbContext<CommonDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton<ICompanyCatalog>(new FakeCompanyCatalog(A, B));
        services.AddScoped<ICompanyContext, CompanyContext>();
        return services.BuildServiceProvider();
    }

    private static string SeedSession(ServiceProvider provider, string? companyCode)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
        var user = new User { Username = "joao", FullName = "João" };
        db.Users.Add(user);
        var sessionId = Guid.NewGuid().ToString();
        db.UserSessions.Add(new UserSession
        {
            SessionId = sessionId, UserId = user.Id, IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddHours(1), CompanyCode = companyCode
        });
        db.SaveChanges();
        return sessionId;
    }

    private static async Task<(HttpContext Http, ICompanyContext Company)> Invoke(ServiceProvider provider, string sessionId)
    {
        var scope = provider.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Headers.Cookie = $"SIAGROB1.Session={sessionId}";

        var middleware = new CookieAuthMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(http, provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICompanyCatalog>(), NullLogger<CookieAuthMiddleware>.Instance);

        return (http, scope.ServiceProvider.GetRequiredService<ICompanyContext>());
    }

    [Fact]
    public async Task Session_company_is_published_in_the_request_scope_and_as_a_claim()
    {
        using var provider = BuildProvider();
        var sessionId = SeedSession(provider, "0002");

        var (http, company) = await Invoke(provider, sessionId);

        Assert.True(http.User.Identity!.IsAuthenticated);
        Assert.Equal("0002", company.Current?.Code);
        Assert.Equal("Server=.;Database=B", company.ConnectionString);
        Assert.Equal("0002", http.User.FindFirst(CompanyClaims.CompanyCode)?.Value);
    }

    [Fact]
    public async Task Legacy_session_without_company_uses_default_company()
    {
        using var provider = BuildProvider();
        var sessionId = SeedSession(provider, null);

        var (_, company) = await Invoke(provider, sessionId);

        Assert.Equal("0001", company.Current?.Code);
    }

    [Fact]
    public async Task Unknown_cookie_leaves_the_request_unresolved()
    {
        using var provider = BuildProvider();

        var (http, company) = await Invoke(provider, Guid.NewGuid().ToString());

        Assert.False(http.User?.Identity?.IsAuthenticated ?? false);
        Assert.False(company.IsResolved);
    }

    [Fact]
    public async Task Session_whose_company_is_missing_is_not_authenticated()
    {
        using var provider = BuildProvider();
        var sessionId = SeedSession(provider, "9999");

        var (http, company) = await Invoke(provider, sessionId);

        Assert.False(http.User?.Identity?.IsAuthenticated ?? false);
        Assert.False(company.IsResolved);
    }
}
