using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Domain.Constants;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Controllers;

public sealed record CompanyLogoRequest(string? ContentType, string? File);

/// <summary>
/// Home por empresa: painel ligado/desligado e logo. "current" é a empresa da sessão (qualquer usuário);
/// as rotas com código são do cadastro de Empresas (só administrador).
/// </summary>
[ApiController]
[Route("api/companies")]
public class CompanyHomeController(
    CommonDbContext db,
    ICompanyContext companyContext,
    CompanyLogoService logoService,
    IUserPermissions userPermissions) : ControllerBase
{
    [HttpGet("current/home")]
    public async Task<IActionResult> GetCurrentHome()
    {
        var code = companyContext.CompanyCode;
        var show = await db.Companies.AsNoTracking().Where(c => c.Code == code)
            .Select(c => (bool?)c.ShowHomeDashboard).FirstOrDefaultAsync();

        // Empresa fora do catálogo (fallback da configuração): mantém o comportamento de antes, com painel.
        return Ok(new { ShowHomeDashboard = show ?? true, HasLogo = await logoService.ExistsAsync(code) });
    }

    [HttpGet("current/logo")]
    public Task<IActionResult> GetCurrentLogo() => LogoResult(companyContext.CompanyCode);

    [HttpGet("{code}/logo")]
    public async Task<IActionResult> GetLogo([FromRoute] string code) =>
        await IsAdminAsync() ? await LogoResult(code) : Forbidden();

    [HttpPost("{code}/logo")]
    public async Task<IActionResult> SetLogo([FromRoute] string code, [FromBody] CompanyLogoRequest request)
    {
        if (!await IsAdminAsync()) return Forbidden();

        try
        {
            await logoService.SetAsync(code, request?.ContentType, request?.File);
            return NoContent();
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    [HttpDelete("{code}/logo")]
    public async Task<IActionResult> RemoveLogo([FromRoute] string code)
    {
        if (!await IsAdminAsync()) return Forbidden();

        await logoService.RemoveAsync(code);
        return NoContent();
    }

    // 204 sem logo: é o estado normal e um 404 sujaria o console a cada visita à home.
    private async Task<IActionResult> LogoResult(string code)
    {
        var logo = await logoService.GetAsync(code);
        return logo is null ? NoContent() : File(logo.Value.Content, logo.Value.ContentType);
    }

    private async Task<bool> IsAdminAsync() =>
        User.IsAdmin() || await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin);

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão de administrador para gerenciar empresas.");
}
