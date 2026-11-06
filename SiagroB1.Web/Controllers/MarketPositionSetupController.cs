using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

public sealed record MarketFxLockInput(bool Locked, decimal? FxRate);

/// <summary>
/// Cadastros de apoio da posição por REST: commodities de exemplo e câmbio fechado dos contratos. Erros em
/// text/plain pt-BR; ambos exigem MARKET_MANAGE.
/// </summary>
[ApiController]
[Authorize]
[MarketModuleEnabled]
[Route("market")]
public class MarketPositionSetupController(
    MarketCommodityService commodities, MarketContractFxLockService fxLock, MarketPermissionGuard guard,
    ILogger<MarketPositionSetupController> logger) : ControllerBase
{
    public const string InvalidBodyMessage = "Informe se o câmbio está fechado e, se for o caso, a taxa.";

    public const string InvalidSideMessage = "Informe o lado do contrato: purchase ou sales.";

    private string UserName => User.Identity?.Name ?? string.Empty;

    [HttpPost("commodities/samples")]
    public Task<IActionResult> CreateSamples(CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await guard.EnsureAsync(UserName, PermissionCodes.MarketManage);
            return Ok(new { created = await commodities.CreateSamplesAsync(cancellationToken) });
        });

    [HttpPost("contracts/{side}/{key:guid}/fx-lock")]
    public Task<IActionResult> SetFxLock(string side, Guid key, [FromBody] MarketFxLockInput? input,
        CancellationToken cancellationToken)
    {
        MarketEfpContractSide? parsed = side.Trim().ToLowerInvariant() switch
        {
            "purchase" => MarketEfpContractSide.Purchase,
            "sales" => MarketEfpContractSide.Sales,
            _ => null,
        };
        if (parsed is null)
            return Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidSideMessage));
        if (input is null)
            return Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidBodyMessage));

        return Run(async () =>
        {
            await fxLock.SetAsync(parsed.Value, key, input.Locked, input.FxRate, UserName, cancellationToken);
            return NoContent();
        });
    }

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException e)
        {
            return MarketTextResults.NotFound(e.Message);
        }
        catch (ForbiddenException e)
        {
            return MarketTextResults.Forbidden(e.Message);
        }
        catch (BusinessException e)
        {
            return MarketTextResults.BadRequest(e.Message);
        }
        catch (DbUpdateException e)
        {
            logger.LogError(e, "Falha ao gravar o cadastro da posição do Mercado.");
            return MarketTextResults.BadRequest(
                MarketODataController<Domain.Entities.Market.MarketCommodity, string>.DescribeSaveError(e)
                ?? "Não foi possível gravar o registro. Confira os dados e tente de novo.");
        }
    }
}
