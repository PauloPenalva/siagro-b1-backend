using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Security.Authentication;

namespace SiagroB1.Gateway.Controllers;

[ApiController]
[Route("security/auth")]
[AllowAnonymous] 
public class AuthController(
    BasicAuthenticationHandler authHandler,
    ILogger<AuthController> logger
    ) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        
        var response = new
        {
            Authenticated = isAuthenticated,
            Username = User.Identity?.Name,
            IsAdmin = User.HasClaim("IsAdmin", "True"),
            Companies = User.Claims
                .Where(c => c.Type == "HasAccessToCompany")
                .Select(c => c.Value)
                .ToList(),
            CurrentCompany = Request.Cookies["CurrentCompanyCode"]
        };
        
        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await authHandler.LogoutAsync();
        return Ok(new { success = true, message = "Logged out" });
    }

    // [HttpPost("company/{companyCode}")]
    // [Authorize] // Apenas autenticados
    // public async Task<IActionResult> SelectCompany(string companyCode)
    // {
    //     try
    //     {
    //         await _companyService.SetCurrentCompanyAsync(companyCode);
    //         return Ok(new { 
    //             success = true, 
    //             message = $"Company '{companyCode}' selected" 
    //         });
    //     }
    //     catch (Exception ex)
    //     {
    //         return BadRequest(new { error = ex.Message });
    //     }
    // }
}