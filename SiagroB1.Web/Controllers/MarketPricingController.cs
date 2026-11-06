using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Filters;

namespace SiagroB1.Web.Controllers;

/// <summary>Módulo Mercado: parâmetros de negociação e cidades com frete. Leitura livre; a permissão da gravação é do serviço.</summary>
[ApiController]
[Authorize]
[MarketModuleEnabled]
[Route("market")]
public class MarketPricingController(MarketTradingSettingsService settings, MarketFreightRateService freightRates)
    : ControllerBase
{
    [HttpGet("trading-settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        Ok(await settings.GetAsync(cancellationToken));

    [HttpPut("trading-settings")]
    public async Task<IActionResult> SaveSettings([FromBody] MarketTradingSettingsInput input, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await settings.SaveAsync(input, User.Identity?.Name ?? string.Empty, cancellationToken));
        }
        catch (ForbiddenException e)
        {
            return StatusCode(StatusCodes.Status403Forbidden, e.Message);
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet("freight-cities")]
    public async Task<IActionResult> FreightCities(CancellationToken cancellationToken) =>
        Ok(await freightRates.GetCitiesAsync(cancellationToken));
}
