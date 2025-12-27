using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Security.Dtos;
using SiagroB1.Security.Interfaces;

namespace SiagroB1.Gateway.Controllers;

[ApiController]
[Route("security/auth")]
[AllowAnonymous]
public class AuthController(
    IAuthService _authService,
    ILogger<AuthController> _logger,
    IConfiguration _configuration
    ) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Username) || 
            string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { message = "Usuário e senha são obrigatórios" });
        }

        var result = await _authService.LoginAsync(request.Username, request.Password);

        if (result.Success)
        {
            _logger.LogInformation("Login bem-sucedido para: {Username}", request.Username);
            return Ok(new LoginResponse
            {
                Success = true,
                Message = result.Message,
                User = result.User,
                SessionId = result.SessionId,
                ExpiresAt = result.ExpiresAt
            });
        }

        _logger.LogWarning("Login falhou para: {Username}", request.Username);
        return Unauthorized(new { message = result.Message });
    }
    
    
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        try
        {
            // Obter sessionId do cookie
            if (Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId))
            {
                await _authService.LogoutAsync(sessionId);
            }

            // Remover cookies
            Response.Cookies.Delete("SIAGROB1.Session");
            Response.Cookies.Delete("SIAGROB1.User");

            _logger.LogInformation("Logout realizado para: {Username}", User.Identity?.Name);
            
            return Ok(new
            {
                Success = true,
                Message = "Logout realizado com sucesso"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro durante logout");
            return StatusCode(500, new { message = "Erro ao realizar logout" });
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        
        if (!isAuthenticated)
        {
            // Verificar cookies
            if (Request.Cookies.TryGetValue("SIAGROB1.User", out var userCookie))
            {
                try
                {
                    var decodedValue = Uri.UnescapeDataString(userCookie);
                    using var doc = System.Text.Json.JsonDocument.Parse(decodedValue);
                    
                    var username = doc.RootElement.GetProperty("Username").GetString();
                    if (!string.IsNullOrEmpty(username))
                    {
                        var userInfo = await _authService.GetUserInfoAsync(username);
                        if (userInfo != null)
                        {
                            return Ok(new
                            {
                                Authenticated = true,
                                Username = userInfo.Username,
                                FullName = userInfo.FullName,
                                IsAdmin = userInfo.IsAdmin,
                                FromCookie = true
                            });
                        }
                    }
                }
                catch
                {
                    // Ignorar erro
                }
            }
            
            return Ok(new { Authenticated = false });
        }
        
        // Se está autenticado via User.Identity
        var usernameFromClaims = User.Identity?.Name;
        UserInfo? userInfoFromDb = null;
        
        if (!string.IsNullOrEmpty(usernameFromClaims))
        {
            userInfoFromDb = await _authService.GetUserInfoAsync(usernameFromClaims);
        }

        return Ok(new
        {
            Authenticated = true,
            Username = User.Identity?.Name,
            FullName = userInfoFromDb?.FullName ?? User.FindFirst(ClaimTypes.GivenName)?.Value,
            IsAdmin = User.HasClaim("IsAdmin", "True"),
            Claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
            SessionId = Request.Cookies["SIAGROB1.Session"],
            FromPrincipal = true
        });
    }

    [HttpGet("info")]
    public IActionResult GetSystemInfo()
    {
        return Ok(new
        {
            Application = "SIAGRO B1 Gateway",
            Version = _configuration["Version"] ?? "1.0.0",
            Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Development",
            RequiresAuthentication = true,
            AuthenticationMethods = new[] { "Basic", "Cookie" },
            Supports = new[] { "Login", "Logout", "Session Management" },
            Timestamp = DateTime.UtcNow
        });
    }

    // Endpoint para Basic Auth (compatibilidade)
    [HttpPost("login/basic")]
    public async Task<IActionResult> LoginBasic([FromHeader(Name = "Authorization")] string authorization)
    {
        if (string.IsNullOrEmpty(authorization))
        {
            return BadRequest(new { message = "Header Authorization é obrigatório" });
        }

        var result = await _authService.LoginWithBasicAuthAsync(authorization);

        if (result.Success)
        {
            return Ok(new LoginResponse
            {
                Success = true,
                Message = result.Message,
                User = result.User,
                SessionId = result.SessionId,
                ExpiresAt = result.ExpiresAt
            });
        }

        return Unauthorized(new { message = result.Message });
    }
}
