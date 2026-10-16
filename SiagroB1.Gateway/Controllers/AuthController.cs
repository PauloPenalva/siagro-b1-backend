using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra.Companies;
using SiagroB1.Security.Dtos;
using SiagroB1.Security.Interfaces;
using SiagroB1.Security.Services;
using SiagroB1.Security.Shared;

namespace SiagroB1.Gateway.Controllers;

[ApiController]
[Route("security/auth")]
[AllowAnonymous]
public class AuthController(
    IAuthService authService,
    ILogger<AuthController> logger,
    IConfiguration configuration,
    // BranchService NÃO é injetado no construtor: ele depende do AppDbContext, cujas opções leem a
    // empresa da sessão na ativação do controller — no login isso acontece antes de AuthService.Set
    // e caía no fallback. Só os endpoints de filial o resolvem, e esses já chegam com o cookie.
    IServiceProvider serviceProvider,
    MenuService menuService,
    IPasswordResetService passwordResetService,
    PasswordPolicy passwordPolicy,
    ICompanyContext companyContext
    ) : ControllerBase
{
    /// <summary>
    /// Pede o link de redefinição de senha.
    ///
    /// Responde sempre 200 com a mesma mensagem, exista ou não a conta: qualquer diferença
    /// transformaria este endpoint público num verificador de usuários válidos.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await passwordResetService.RequestAsync(
            request?.UsernameOrEmail ?? string.Empty,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new
        {
            Success = true,
            Message = "Se o usuário informado existir e tiver e-mail cadastrado, " +
                      "enviaremos um link para redefinição de senha."
        });
    }

    /// <summary>Diz se o link ainda vale, para a tela não abrir um formulário que já vai falhar.</summary>
    [HttpGet("reset-password/validate")]
    public async Task<IActionResult> ValidateResetToken([FromQuery] string? token)
    {
        return Ok(new
        {
            Valid = await passwordResetService.ValidateAsync(token ?? string.Empty),
            passwordResetService.PasswordRequirements
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await passwordResetService.ResetAsync(
            request?.Token ?? string.Empty,
            request?.NewPassword ?? string.Empty);

        if (!result.Success)
        {
            return BadRequest(new { message = result.Message });
        }

        // A senha mudou: os cookies desta máquina apontam para uma sessão que acabou de ser
        // invalidada, e deixá-los no browser só produziria um 401 na próxima navegação.
        Response.Cookies.Delete("SIAGROB1.Session");
        Response.Cookies.Delete("SIAGROB1.User");

        return Ok(new { Success = true, result.Message });
    }

    /// <summary>Lista do seletor de empresa da tela de login. Anônimo e mínimo (código e nome).</summary>
    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies() => Ok(await authService.ListCompaniesAsync());

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { message = "Usuário e senha são obrigatórios" });
        }

        var result = await authService.LoginAsync(request.Username, request.Password, request.CompanyCode);

        if (result.Success)
        {
            logger.LogInformation("Login bem-sucedido para: {Username} na empresa {Company}", request.Username, result.User?.CompanyCode);
            return Ok(new LoginResponse
            {
                Success = true,
                Message = result.Message,
                User = result.User,
                SessionId = result.SessionId,
                ExpiresAt = result.ExpiresAt,
                PasswordRequirements = passwordPolicy.Description
            });
        }

        logger.LogWarning("Login falhou para: {Username}", request.Username);
        return Unauthorized(new { message = result.Message });
    }
    
    
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            // Obter sessionId do cookie
            if (Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId))
            {
                await authService.LogoutAsync(sessionId);
            }

            // Remover cookies
            Response.Cookies.Delete("SIAGROB1.Session");
            Response.Cookies.Delete("SIAGROB1.User");

            logger.LogInformation("Logout realizado para: {Username}", User.Identity?.Name);
            
            return Ok(new
            {
                Success = true,
                Message = "Logout realizado com sucesso"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro durante logout");
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
                        var userInfo = await authService.GetUserInfoAsync(username);
                        if (userInfo != null)
                        {
                            return Ok(new
                            {
                                Authenticated = true,
                                Username = userInfo.Username,
                                FullName = userInfo.FullName,
                                Email = userInfo.Email,
                                IsAdmin = userInfo.IsAdmin,
                                Theme = userInfo.Theme,
                                HasPhoto = userInfo.HasPhoto,
                                // Sem isto a tela perde as permissões em todo F5: o /status é a
                                // única fonte de identidade depois do boot, e o login só acontece
                                // uma vez por sessão.
                                Permissions = userInfo.Permissions,
                                Roles = userInfo.Roles,
                                CompanyCode = userInfo.CompanyCode,
                                CompanyName = userInfo.CompanyName,
                                // Mesma razão das permissões: a tela que pede senha precisa
                                // mostrar a regra vigente, e a política é configurável.
                                PasswordRequirements = passwordPolicy.Description,
                                FromCookie = true,
                                UserId = userInfo.Id,
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
            userInfoFromDb = await authService.GetUserInfoAsync(usernameFromClaims);
        }

        return Ok(new
        {
            Authenticated = true,
            Username = User.Identity?.Name,
            FullName = userInfoFromDb?.FullName ?? User.FindFirst(ClaimTypes.GivenName)?.Value,
            Email = userInfoFromDb?.Email ?? User.FindFirst(ClaimTypes.Email)?.Value,
            IsAdmin = User.HasClaim("IsAdmin", "True"),
            Theme = userInfoFromDb?.Theme,
            HasPhoto = userInfoFromDb?.HasPhoto ?? false,
            Permissions = userInfoFromDb?.Permissions ?? [],
            Roles = userInfoFromDb?.Roles ?? [],
            CompanyCode = userInfoFromDb?.CompanyCode,
            CompanyName = userInfoFromDb?.CompanyName,
            PasswordRequirements = passwordPolicy.Description,
            Claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
            SessionId = Request.Cookies["SIAGROB1.Session"],
            FromPrincipal = true,
            UserId = userInfoFromDb?.Id,
        });
    }

    [HttpGet("info")]
    public IActionResult GetSystemInfo()
    {
        return Ok(new
        {
            Application = "SIAGRO B1",
            Version = configuration["Version"] ?? "1.0.0",
            Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Development",
            RequiresAuthentication = true,
            AuthenticationMethods = new[] { "Basic", "Cookie" },
            Supports = new[] { "Login", "Logout", "Session Management" },
            Timestamp = DateTime.UtcNow,
            CompanyName = companyContext.Current?.CompanyName ?? configuration["CompanyName"] ?? "COMPANY NAME",
            CompanyCode = companyContext.Current?.Code,
            // Modo de integração: em SAPB1 o cadastro de usuários é mantido no SAP, e as telas
            // precisam saber disso para não oferecer uma edição que a sincronização vai desfazer.
            Erp = configuration["Erp"] ?? "STANDALONE",
        });
    }

    // Endpoint para Basic Auth (compatibilidade)
    [HttpPost("login/basic")]
    public async Task<IActionResult> LoginBasic([FromHeader(Name = "Authorization")] string authorization, [FromHeader(Name = "X-Company")] string? companyCode)
    {
        if (string.IsNullOrEmpty(authorization))
        {
            return BadRequest(new { message = "Header Authorization é obrigatório" });
        }

        var result = await authService.LoginWithBasicAuthAsync(authorization, companyCode);

        if (result.Success)
        {
            return Ok(new LoginResponse
            {
                Success = true,
                Message = result.Message,
                User = result.User,
                SessionId = result.SessionId,
                ExpiresAt = result.ExpiresAt,
                PasswordRequirements = passwordPolicy.Description
            });
        }

        return Unauthorized(new { message = result.Message });
    }

    [HttpPost("SetDefaultBranch")]
    public async Task<IActionResult> SetDefaultBranch([FromBody] SetDefaultBranchRequest branchRequest)
    {
        if (!Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId)) 
            return BadRequest("SessionId not found.");
        
        try
        {
            await serviceProvider.GetRequiredService<BranchService>().SetDefaultBranch(sessionId, branchRequest.BranchCode);
            return Ok();
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
    
    [HttpGet("GetBranchInfo")]
    public async Task<IActionResult> GetDefaultBranchInfo()
    {
        if (!Request.Cookies.TryGetValue("SIAGROB1.Session", out var sessionId)) 
            return BadRequest("SessionId not found.");
        
        try
        {
            Request.Cookies.TryGetValue("SIAGROB1.User", out var userCookie);
                
            var branchInfo = await serviceProvider.GetRequiredService<BranchService>().GetDefaultBranchInfo(sessionId);
            return Ok(branchInfo);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet("GetUserMenu")]
    public async Task<IActionResult> GetUserMenu()
    {
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        if (!isAuthenticated)
            return Unauthorized(new { message = "User not authenticated." });
        
        try
        {   
            var usernameFromClaims = User.Identity?.Name;
            UserInfo? userInfoFromDb = null;
        
            if (!string.IsNullOrEmpty(usernameFromClaims))
            {
                userInfoFromDb = await authService.GetUserInfoAsync(usernameFromClaims);
            }

            if (userInfoFromDb == null)
                return NotFound(new { message = "User not found." });
            
            var userMenu = await menuService.GetMenuAsync(Guid.Parse(userInfoFromDb.Id));
            
            return Ok(userMenu);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
    
}
