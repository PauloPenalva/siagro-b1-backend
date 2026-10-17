
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Security.Shared;

namespace SiagroB1.Security.Middlewares;

public class CookieAuthMiddleware
{
    private readonly RequestDelegate _next;

    public CookieAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IServiceScopeFactory scopeFactory,
        ICompanyCatalog catalog,
        ILogger<CookieAuthMiddleware> logger)
    {
        // Se já está autenticado, não precisa fazer nada
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            // Verificar se tem cookie de sessão
            if (context.Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId))
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
                
                // Buscar sessão válida
                var session = await dbContext.UserSessions
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId && 
                                            s.ExpiresAt > DateTime.UtcNow && 
                                            s.IsActive);

                if (session?.User != null)
                {
                    // Empresa da sessão. Sessão anterior ao recurso não tem CompanyCode: cai na padrão.
                    // Gravada no escopo da REQUISIÇÃO (RequestServices), não no escopo interno acima —
                    // é o escopo da requisição que vai resolver o AppDbContext do controller.
                    var company = await catalog.GetByCodeAsync(session.CompanyCode ?? catalog.DefaultCompanyCode);

                    if (company is null)
                    {
                        // Sem empresa resolvida o AppDbContext cairia no banco de fallback: não autentica.
                        logger.LogWarning(
                            "Sessão {SessionId} aponta para a empresa {Code}, que não está cadastrada; sessão não autenticada.",
                            sessionId, session.CompanyCode ?? catalog.DefaultCompanyCode);
                    }
                    else
                    {
                        context.RequestServices.GetRequiredService<ICompanyContext>().Set(company);

                        // Criar identidade do usuário
                        context.User = CreateUserPrincipal(session.User, company.Code);

                        // Atualizar atividade
                        session.LastActivityAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync();
                    }
                }
            }
        }
        
        await _next(context);
    }

    private static ClaimsPrincipal CreateUserPrincipal(User user, string? companyCode)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.FullName),
            new("IsAdmin", user.IsAdmin.ToString()),
        };

        if (companyCode is not null)
        {
            claims.Add(new Claim(CompanyClaims.CompanyCode, companyCode));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"));
    }
}

// Extensão para uso fácil
public static class CookieAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseCookieAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CookieAuthMiddleware>();
    }
}
