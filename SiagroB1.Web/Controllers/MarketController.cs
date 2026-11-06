using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Constants;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

public sealed record DailyCloseInput(string Code, DateOnly Date, decimal Close);

/// <summary>Módulo Mercado: cotações ao vivo, lançamento manual e conexão com a CMA.</summary>
[ApiController]
[Authorize]
[MarketModuleEnabled]
[Route("market")]
public class MarketController(
    MarketQuoteService quotes,
    MarketDailyCloseService dailyCloses,
    MarketInstrumentService instruments,
    CmaSettingsService cmaSettings,
    LiveQuoteStore store,
    MarketQuotesSseWriter sse,
    ICompanyContext companyContext,
    IUserPermissions permissions) : ControllerBase
{
    private string UserName => User.Identity?.Name ?? string.Empty;

    [HttpGet("quotes/live")]
    public Task Live(CancellationToken cancellationToken) => sse.RunAsync(HttpContext, companyContext, cancellationToken);

    [HttpGet("quotes")]
    public async Task<IActionResult> Board(CancellationToken cancellationToken) =>
        Ok(await quotes.GetBoardAsync(cancellationToken));

    [HttpPost("quotes/{code}/manual")]
    public Task<IActionResult> SetManual(string code, [FromBody] ManualQuoteInput input, CancellationToken cancellationToken) =>
        Run(() => quotes.SetManualAsync(code, input, UserName, cancellationToken));

    [HttpPost("quotes/{code}/release")]
    public Task<IActionResult> Release(string code, CancellationToken cancellationToken) =>
        Run(() => quotes.ReleaseManualAsync(code, cancellationToken));

    [HttpPost("daily-closes")]
    public Task<IActionResult> SetDailyClose([FromBody] DailyCloseInput input, CancellationToken cancellationToken) =>
        Run(() => dailyCloses.SetManualCloseAsync(input.Code, input.Date, input.Close, UserName, cancellationToken));

    [HttpPost("instruments/samples")]
    public async Task<IActionResult> CreateSamples(CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync())
            return Forbidden();

        return Ok(new { created = await instruments.CreateSamplesAsync(cancellationToken) });
    }

    [HttpGet("cma-settings")]
    public async Task<IActionResult> GetCmaSettings(CancellationToken cancellationToken) =>
        Ok(await cmaSettings.GetAsync(cancellationToken));

    [HttpPut("cma-settings")]
    public async Task<IActionResult> SaveCmaSettings([FromBody] CmaSettingsInput input, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync())
            return Forbidden();

        try
        {
            var saved = await cmaSettings.SaveAsync(input, cancellationToken);
            // O conector relê as credenciais ao reconectar.
            store.RequestReconnect(companyContext.CompanyCode);
            return Ok(saved);
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
        catch (DefaultException)
        {
            // Nfe:CertificateKey malformada (não é base64 de 32 bytes): a cifra recusa a chave.
            return BadRequest("Chave de criptografia (Nfe:CertificateKey) inválida neste ambiente; corrija-a antes de salvar a senha.");
        }
    }

    [HttpPost("cma/reconnect")]
    public async Task<IActionResult> Reconnect()
    {
        if (!await IsAdminAsync())
            return Forbidden();

        store.RequestReconnect(companyContext.CompanyCode);
        return Accepted();
    }

    private async Task<bool> IsAdminAsync() => await permissions.HasRoleAsync(UserName, Roles.Admin);

    // Forbid() sem corpo vira erro genérico no front; a mensagem no corpo deixa mostrar o motivo.
    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão de administrador para esta operação.");

    private async Task<IActionResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return NoContent();
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }
}
