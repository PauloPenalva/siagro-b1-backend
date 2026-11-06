using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using SiagroB1.Web.Base;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

public sealed record MarketFxHedgeSettleInput(decimal Rate);

/// <summary>
/// Hedges de moeda: leitura por OData (só GET, <c>$expand=Account($expand=Counterparty)</c>) e gravações por REST em
/// <c>market/fx-hedges</c> (MARKET_MANAGE checado no serviço; erros em text/plain pt-BR).
/// </summary>
[Authorize]
[MarketModuleEnabled]
public class MarketFxHedgesController(
    AppDbContext context, MarketFxHedgeService service, ILogger<MarketFxHedgesController> logger) : ODataController
{
    public const string InvalidBodyMessage = "Dados do hedge inválidos ou ausentes.";

    public const string SaveFailedMessage = "Não foi possível gravar o hedge. Confira os dados e tente de novo.";

    private string UserName => User.Identity?.Name ?? string.Empty;

    [EnableQuery(PageSize = 200, MaxExpansionDepth = 3)]
    public IActionResult Get() => Ok(context.MarketFxHedges.AsNoTracking());

    /// <summary>SingleResult sobre consulta: o $expand (Account) roda no banco.</summary>
    [EnableQuery(MaxExpansionDepth = 3)]
    public async Task<IActionResult> Get([FromRoute] int key)
    {
        var query = context.MarketFxHedges.AsNoTracking().Where(h => h.Id == key);
        return await query.AnyAsync() ? Ok(SingleResult.Create(query)) : NotFound();
    }

    [HttpPost("market/fx-hedges")]
    public Task<IActionResult> Create([FromBody] MarketFxHedgeInput? input, CancellationToken cancellationToken) =>
        input is null || !ModelState.IsValid
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidBodyMessage))
            : RunHedge(() => service.CreateAsync(input, UserName, cancellationToken));

    [HttpPut("market/fx-hedges/{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] MarketFxHedgeInput? input, CancellationToken cancellationToken) =>
        input is null || !ModelState.IsValid
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidBodyMessage))
            : RunHedge(() => service.UpdateAsync(id, input, UserName, cancellationToken));

    [HttpDelete("market/fx-hedges/{id:int}")]
    public Task<IActionResult> Remove(int id, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await service.DeleteAsync(id, UserName, cancellationToken);
            return NoContent();
        });

    [HttpPost("market/fx-hedges/{id:int}/settle")]
    public Task<IActionResult> Settle(int id, [FromBody] MarketFxHedgeSettleInput? input, CancellationToken cancellationToken) =>
        input is null || !ModelState.IsValid
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest("Informe a taxa de liquidação."))
            : RunHedge(() => service.SettleAsync(id, input.Rate, UserName, cancellationToken));

    [HttpPost("market/fx-hedges/{id:int}/reopen")]
    public Task<IActionResult> Reopen(int id, CancellationToken cancellationToken) =>
        RunHedge(() => service.ReopenAsync(id, UserName, cancellationToken));

    [HttpPost("market/fx-hedges/{id:int}/cancel")]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) =>
        RunHedge(() => service.CancelAsync(id, UserName, cancellationToken));

    [HttpPost("market/fx-hedges/{id:int}/confirm")]
    public Task<IActionResult> Confirm(int id, CancellationToken cancellationToken) =>
        RunHedge(() => service.SetConfirmedAsync(id, true, UserName, cancellationToken));

    [HttpPost("market/fx-hedges/{id:int}/unconfirm")]
    public Task<IActionResult> Unconfirm(int id, CancellationToken cancellationToken) =>
        RunHedge(() => service.SetConfirmedAsync(id, false, UserName, cancellationToken));

    [HttpGet("market/fx-hedges/{id:int}/suggested-rate")]
    public Task<IActionResult> SuggestedRate(int id, CancellationToken cancellationToken) =>
        Run(async () => Json(await service.SuggestSettlementRateAsync(id, cancellationToken)));

    private static JsonResult Json(object value) => new(value, MarketQuotesStreamLoop.JsonOptions);

    /// <summary>Resposta do hedge: a entidade em camelCase, sem a navegação da conta.</summary>
    private Task<IActionResult> RunHedge(Func<Task<MarketFxHedge>> action) =>
        Run(async () =>
        {
            var hedge = await action();
            var node = JsonSerializer.SerializeToNode(hedge, MarketQuotesStreamLoop.JsonOptions)!.AsObject();
            node.Remove("account");
            MarketJsonDates.AsDateOnly(node, "tradeDate", "settlementDate");
            return Json(node);
        });

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
            logger.LogError(e, "Falha ao gravar o hedge de moeda do Mercado.");
            return MarketTextResults.BadRequest(
                MarketODataController<MarketFxHedge, int>.DescribeSaveError(e) ?? SaveFailedMessage);
        }
    }
}
