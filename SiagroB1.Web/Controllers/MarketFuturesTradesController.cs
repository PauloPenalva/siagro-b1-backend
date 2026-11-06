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

public sealed record MarketFuturesConfirmDayInput(int AccountId, DateOnly Date);

/// <summary>
/// Operações de bolsa: leitura por OData (só GET, <c>$expand=Account($expand=Counterparty),Instrument</c>) e as
/// gravações por REST em <c>market/futures-trades</c> (MARKET_MANAGE checado no serviço; erros em text/plain pt-BR).
/// Também a posição aberta e o realizado (<c>market/futures-positions</c>, <c>market/futures-realized</c>).
/// </summary>
[Authorize]
[MarketModuleEnabled]
public class MarketFuturesTradesController(
    AppDbContext context, MarketFuturesTradeService service, MarketDerivativesPositionService positions,
    ILogger<MarketFuturesTradesController> logger) : ODataController
{
    public const string InvalidBodyMessage = "Dados da operação inválidos ou ausentes.";

    public const string SaveFailedMessage = "Não foi possível gravar a operação. Confira os dados e tente de novo.";

    private string UserName => User.Identity?.Name ?? string.Empty;

    [EnableQuery(PageSize = 200, MaxExpansionDepth = 3)]
    public IActionResult Get() => Ok(context.MarketFuturesTrades.AsNoTracking());

    /// <summary>SingleResult sobre consulta: o $expand (Account, Instrument) roda no banco.</summary>
    [EnableQuery(MaxExpansionDepth = 3)]
    public async Task<IActionResult> Get([FromRoute] int key)
    {
        var query = context.MarketFuturesTrades.AsNoTracking().Where(t => t.Id == key);
        return await query.AnyAsync() ? Ok(SingleResult.Create(query)) : NotFound();
    }

    [HttpPost("market/futures-trades")]
    public Task<IActionResult> Create([FromBody] MarketFuturesTradeInput? input, CancellationToken cancellationToken) =>
        input is null || !ModelState.IsValid
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidBodyMessage))
            : RunTrade(() => service.CreateAsync(input, UserName, cancellationToken), cancellationToken);

    [HttpPut("market/futures-trades/{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] MarketFuturesTradeInput? input, CancellationToken cancellationToken) =>
        input is null || !ModelState.IsValid
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidBodyMessage))
            : RunTrade(() => service.UpdateAsync(id, input, UserName, cancellationToken), cancellationToken);

    [HttpDelete("market/futures-trades/{id:int}")]
    public Task<IActionResult> Remove(int id, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await service.DeleteAsync(id, UserName, cancellationToken);
            return NoContent();
        });

    [HttpPost("market/futures-trades/{id:int}/confirm")]
    public Task<IActionResult> Confirm(int id, CancellationToken cancellationToken) =>
        RunTrade(() => service.SetConfirmedAsync(id, true, UserName, cancellationToken), cancellationToken);

    [HttpPost("market/futures-trades/{id:int}/unconfirm")]
    public Task<IActionResult> Unconfirm(int id, CancellationToken cancellationToken) =>
        RunTrade(() => service.SetConfirmedAsync(id, false, UserName, cancellationToken), cancellationToken);

    [HttpPost("market/futures-trades/confirm-day")]
    public Task<IActionResult> ConfirmDay([FromBody] MarketFuturesConfirmDayInput? input, CancellationToken cancellationToken) =>
        input is null || input.AccountId <= 0 || input.Date == default
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest("Informe a conta e a data."))
            : Run(async () => Json(new { confirmed = await service.ConfirmDayAsync(input.AccountId, input.Date, UserName, cancellationToken) }));

    [HttpGet("market/futures-trades/lot-info")]
    public Task<IActionResult> LotInfo(string? instrumentCode, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(instrumentCode)
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest("Informe o instrumento."))
            : Run(async () => Json(await service.GetLotInfoAsync(instrumentCode, cancellationToken)));

    [HttpGet("market/futures-positions")]
    public Task<IActionResult> Positions(CancellationToken cancellationToken) =>
        Run(async () => Json(await positions.GetOpenPositionsAsync(cancellationToken)));

    [HttpGet("market/futures-realized")]
    public Task<IActionResult> Realized(DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        from is null || to is null
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest("Informe o período (de e até)."))
            : Run(async () => Json(await positions.GetRealizedAsync(from.Value, to.Value, cancellationToken)));

    private static JsonResult Json(object value) => new(value, MarketQuotesStreamLoop.JsonOptions);

    /// <summary>Resposta da operação: a entidade em camelCase mais <c>tons</c> (toneladas com sinal; nulo no dólar).</summary>
    private Task<IActionResult> RunTrade(Func<Task<MarketFuturesTrade>> action, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            var trade = await action();
            var instrument = await context.MarketInstruments.AsNoTracking()
                .FirstAsync(i => i.Code == trade.InstrumentCode, cancellationToken);
            var node = JsonSerializer.SerializeToNode(trade, MarketQuotesStreamLoop.JsonOptions)!.AsObject();
            node.Remove("account");
            node.Remove("instrument");
            MarketJsonDates.AsDateOnly(node, "tradeDate");
            node["tons"] = MarketFuturesTradeService.SignedTons(trade, instrument);
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
            logger.LogError(e, "Falha ao gravar a operação de bolsa do Mercado.");
            return MarketTextResults.BadRequest(
                MarketODataController<MarketFuturesTrade, int>.DescribeSaveError(e) ?? SaveFailedMessage);
        }
    }
}

/// <summary>Casamentos PEPS, só leitura por OData.</summary>
[Authorize]
[MarketModuleEnabled]
public class MarketFuturesMatchesController(AppDbContext context) : ODataController
{
    [EnableQuery(PageSize = 500)]
    public IActionResult Get() => Ok(context.MarketFuturesMatches.AsNoTracking());

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] int key)
    {
        var query = context.MarketFuturesMatches.AsNoTracking().Where(m => m.Id == key);
        return await query.AnyAsync() ? Ok(SingleResult.Create(query)) : NotFound();
    }
}
