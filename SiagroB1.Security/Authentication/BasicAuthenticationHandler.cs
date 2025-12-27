using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;


namespace SiagroB1.Security.Authentication;

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly CommonDbContext _context;
    private readonly ILogger<BasicAuthenticationHandler> _logger;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        CommonDbContext context)
        : base(options, logger, encoder, clock)
    {
        _context = context;
        _logger = logger.CreateLogger<BasicAuthenticationHandler>();
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            // 1. Verificar se o header de autorização existe
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"Siagro B1\", charset=\"UTF-8\"";
                return AuthenticateResult.Fail("Authentication required");
            }
            
            
            // 2. Extrair credenciais Basic Auth
            var authHeader = Request.Headers["Authorization"].ToString();
            if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticateResult.Fail("Invalid Authorization Header");
            }

            // 3. Decodificar base64
            var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var separatorIndex = credentials.IndexOf(':');
            
            if (separatorIndex == -1)
            {
                return AuthenticateResult.Fail("Invalid Basic Authentication Format");
            }

            var username = credentials.Substring(0, separatorIndex);
            var password = credentials.Substring(separatorIndex + 1);

            // 4. Validar usuário
            var user = await ValidateUserAsync(username, password);
            if (user == null)
            {
                _logger.LogWarning("Failed login attempt for user: {Username}", username);
                return AuthenticateResult.Fail("Invalid Username or Password");
            }

            // 5. Atualizar último login
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // 6. Criar claims (informações do usuário)
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.GivenName, user.FullName),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim("IsAdmin", user.IsAdmin.ToString())
            };

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

    private async Task<User?> ValidateUserAsync(string username, string password)
    {
        // Buscar usuário
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null) return null;

        // Verificar senha (hash)
        var isValidPassword = VerifyPassword(password, user.PasswordHash);
        
        return isValidPassword ? user : null;
    }

    private bool VerifyPassword(string password, string passwordHash)
    {
        // Implementação simples - você pode usar BCrypt ou Identity
        // Por enquanto, vamos usar uma verificação básica
        // IMPORTANTE: Para produção, use BCrypt.Net ou similar
        return passwordHash == HashPassword(password);
    }

    private string HashPassword(string password)
    {
        // Hash básico - SUBSTITUA por BCrypt em produção!
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}