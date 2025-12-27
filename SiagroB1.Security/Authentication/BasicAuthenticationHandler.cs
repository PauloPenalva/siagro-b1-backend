using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Shared;


namespace SiagroB1.Security.Authentication;

public class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISystemClock clock,
    CommonDbContext context,
    IHttpContextAccessor httpContextAccessor)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder, clock)
{
    private readonly ILogger<BasicAuthenticationHandler> _logger = logger.CreateLogger<BasicAuthenticationHandler>();
    
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        try
        {
            // ✅ 1. PRIMEIRO: Tentar autenticação por Cookie (se já logado)
            var cookieAuthResult = await TryAuthenticateFromCookieAsync();
            if (cookieAuthResult?.Succeeded == true)
            {
                return cookieAuthResult;
            }

            // ✅ 2. SEGUNDO: Tentar Basic Auth (login inicial)
            if (httpContext?.Request.Headers.ContainsKey("Authorization") == false)
            {
                // Forçar caixa de login do browser
                httpContext.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Siagro B1\", charset=\"UTF-8\"";
                return AuthenticateResult.Fail("Authentication required");
            }

            var authHeader = httpContext?.Request.Headers["Authorization"].ToString();
            if (authHeader?.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)==false)
            {
                return AuthenticateResult.Fail("Invalid Authorization Header");
            }

            // Decodificar credenciais
            var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var separatorIndex = credentials.IndexOf(':');
            
            if (separatorIndex == -1)
            {
                return AuthenticateResult.Fail("Invalid Basic Authentication Format");
            }

            var username = credentials.Substring(0, separatorIndex);
            var password = credentials.Substring(separatorIndex + 1);

            // Validar usuário
            var user = await ValidateUserAsync(username, password);
            if (user == null)
            {
                _logger.LogWarning("Failed login attempt for user: {Username}", username);
                return AuthenticateResult.Fail("Invalid Username or Password");
            }

            // ✅ 3. CRIAR COOKIES DE SESSÃO
            await CreateSessionCookiesAsync(user);

            // Atualizar último login
            user.LastLoginAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            // Criar claims
            var claims = CreateUserClaims(user);
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            _logger.LogInformation("User authenticated: {Username}", username);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error");
            return AuthenticateResult.Fail("Authentication Error");
        }
    }

    private async Task<AuthenticateResult?> TryAuthenticateFromCookieAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        
        try
        {
            // Verificar se tem cookie de sessão
            if (httpContext?.Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId) == false)
            {
                return null;
            }
            
            sessionId = httpContext?.Request.Cookies["SIAGROB1.Session"];
            
            // Buscar sessão no banco (opcional, pode usar cache)
            var session = await context.UserSessions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && 
                                        s.ExpiresAt > DateTime.UtcNow && 
                                        s.IsActive);

            if (session == null || session.User == null)
            {
                // Sessão expirada ou inválida
                Response.Cookies.Delete("SIAGROB1.Session");
                return null;
            }

            // Atualizar última atividade
            session.LastActivityAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            // Criar claims do usuário
            var claims = CreateUserClaims(session.User);
            
            // Adicionar claim da sessão
            claims.Add(new Claim("SessionId", sessionId));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating from cookie");
            return null;
        }
    }

    private async Task CreateSessionCookiesAsync(User user)
    {
        // ✅ 1. Criar Session ID
        var sessionId = Guid.NewGuid().ToString();
        
        // ✅ 2. Salvar sessão no banco
        var session = new UserSession
        {
            SessionId = sessionId,
            UserId = user.Id,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            CreatedAt = DateTime.Now,
            LastActivityAt = DateTime.Now,
            ExpiresAt = DateTime.Now.AddHours(8), // 8 horas de sessão
            IsActive = true
        };
        
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();

        // ✅ 3. Criar Cookie de Sessão
        var sessionCookieOptions = new CookieOptions
        {
            HttpOnly = true, // Não acessível via JavaScript (mais seguro)
            Secure = Request.IsHttps, // Apenas HTTPS em produção
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.Now.AddHours(8),
            Path = "/"
        };
        
        Response.Cookies.Append("SIAGROB1.Session", sessionId, sessionCookieOptions);

        // ✅ 4. Criar Cookie com informações do usuário (não sensíveis)
        var userInfo = new
        {
            Username = user.Username,
            FullName = user.FullName,
            IsAdmin = user.IsAdmin
        };
        
        var serializedUserInfo = JsonSerializer.Serialize(userInfo);
        var encodedUserInfo = Uri.EscapeDataString(serializedUserInfo);
        
        var userCookieOptions = new CookieOptions
        {
            HttpOnly = false, // Pode ser lido pelo JavaScript
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.Now.AddHours(8),
            Path = "/"
        };
        
        Response.Cookies.Append("SIAGROB1.User", 
            encodedUserInfo, 
            userCookieOptions);

        // ✅ 5. Se usuário tem empresa padrão, selecionar automaticamente
        // if (!string.IsNullOrEmpty(user.DefaultCompanyCode))
        // {
        //     try
        //     {
        //         await _companyService.SetCurrentCompanyAsync(user.DefaultCompanyCode);
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogWarning(ex, "Could not set default company for user {Username}", 
        //             user.Username);
        //     }
        // }

        _logger.LogDebug("Session cookies created for user: {Username}", user.Username);
    }

    private List<Claim> CreateUserClaims(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.GivenName, user.FullName),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim("IsAdmin", user.IsAdmin.ToString()),
            //new Claim("DefaultCompanyCode", user.DefaultCompanyCode ?? string.Empty)
        };

        // // Adicionar empresas que o usuário tem acesso
        // var userCompanies = context.UserCompanyAccesses
        //     .Where(uca => uca.UserId == user.Id && uca.IsActive)
        //     .Include(uca => uca.Company)
        //     .Select(uca => uca.Company!.CompanyCode)
        //     .ToList();
        //
        // foreach (var companyCode in userCompanies)
        // {
        //     claims.Add(new Claim("HasAccessToCompany", companyCode));
        // }

        return claims;
    }

    private async Task<User?> ValidateUserAsync(string username, string password)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null) return null;

        // Use BCrypt em produção!
        // return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        return user.PasswordHash == HashPassword(password) ? user : null;
    }

    private string HashPassword(string password)
    {
        return Utils.HashPassword(password);
    }

    // ✅ Para logout (chamado pelo controller)
    public async Task LogoutAsync()
    {   
        var httpContext = httpContextAccessor.HttpContext;
        
        if (httpContext?.Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId)==true)
        {
            // Invalidar sessão no banco
            var session = await context.UserSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId);
            
            if (session != null)
            {
                session.IsActive = false;
                session.ExpiresAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }

            // Remover cookies
            httpContext.Response.Cookies.Delete("SIAGROB1.Session");
            httpContext.Response.Cookies.Delete("SIAGROB1.User");
            //Response.Cookies.Delete("CurrentCompanyCode");
        }
    }
}