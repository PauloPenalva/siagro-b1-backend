
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SiagroB1.Domain.Entities.Common;

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
            CommonDbContext dbContext)
        {
            // Se já está autenticado, não precisa fazer nada
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                // Verificar se tem cookie de sessão
                if (context.Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId))
                {
                    // Buscar sessão válida
                    var session = await dbContext.UserSessions
                        .Include(s => s.User)
                        .FirstOrDefaultAsync(s => s.SessionId == sessionId && 
                                                s.ExpiresAt > DateTime.UtcNow && 
                                                s.IsActive);

                    if (session?.User != null)
                    {
                        // Criar identidade do usuário
                        context.User = CreateUserPrincipal(session.User);
                        
                        // Atualizar atividade
                        session.LastActivityAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync();
                    }
                }
            }
            
            await _next(context);
        }

        private static ClaimsPrincipal CreateUserPrincipal(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.GivenName, user.FullName),
                new Claim("IsAdmin", user.IsAdmin.ToString())
            };
            
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
