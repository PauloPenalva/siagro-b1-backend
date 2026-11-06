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
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using SiagroB1.Web.Base;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// EFP: leitura por OData (só GET, <c>$expand=Account($expand=Counterparty),Instrument</c>) e operações por REST em
/// <c>market/efps</c> (MARKET_MANAGE checado no serviço; erros em text/plain pt-BR).
/// </summary>
[Authorize]
[MarketModuleEnabled]
public class MarketEfpsController(
    AppDbContext context, MarketEfpService service, ILogger<MarketEfpsController> logger) : ODataController
{
    public const string InvalidBodyMessage = "Dados do EFP inválidos ou ausentes.";

    public const string SaveFailedMessage = "Não foi possível gravar o EFP. Confira os dados e tente de novo.";

    public const string UnexpectedMessage =
        "Não foi possível concluir a operação do EFP. Atualize a lista e tente de novo.";

    public const string EfpIdHeader = "X-Market-Efp-Id";

    private string UserName => User.Identity?.Name ?? string.Empty;

    [EnableQuery(PageSize = 200, MaxExpansionDepth = 3)]
    public IActionResult Get() => Ok(context.MarketEfps.AsNoTracking());

    /// <summary>SingleResult sobre consulta: o $expand (Account, Instrument) roda no banco.</summary>
    [EnableQuery(MaxExpansionDepth = 3)]
    public async Task<IActionResult> Get([FromRoute] int key)
    {
        var query = context.MarketEfps.AsNoTracking().Where(e => e.Id == key);
        return await query.AnyAsync() ? Ok(SingleResult.Create(query)) : NotFound();
    }

    [HttpPost("market/efps/preview")]
    public Task<IActionResult> Preview([FromBody] MarketEfpInput? input, CancellationToken cancellationToken) =>
        input is null || !ModelState.IsValid
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidBodyMessage))
            : Run(async () => Json(await service.PreviewAsync(input, cancellationToken)));

    [HttpPost("market/efps")]
    public Task<IActionResult> Create([FromBody] MarketEfpInput? input, CancellationToken cancellationToken) =>
        input is null || !ModelState.IsValid
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest(InvalidBodyMessage))
            : RunEfp(() => service.CreateAsync(input, UserName, cancellationToken));

    [HttpPost("market/efps/{id:int}/resume")]
    public Task<IActionResult> Resume(int id, CancellationToken cancellationToken) =>
        RunEfp(() => service.ResumeAsync(id, UserName, cancellationToken));

    [HttpPost("market/efps/{id:int}/cancel")]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) =>
        RunEfp(() => service.CancelAsync(id, UserName, cancellationToken));

    [HttpGet("market/efps/contracts")]
    public Task<IActionResult> Contracts([FromQuery] string? side, [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        MarketEfpContractSide? parsed = side?.Trim().ToLowerInvariant() switch
        {
            "purchase" => MarketEfpContractSide.Purchase,
            "sales" => MarketEfpContractSide.Sales,
            _ => null,
        };
        return parsed is null
            ? Task.FromResult<IActionResult>(MarketTextResults.BadRequest("Informe o lado do contrato: purchase ou sales."))
            : Run(async () => Json(await service.ListContractsAsync(parsed.Value, search, cancellationToken)));
    }

    private static JsonResult Json(object value) => new(value, MarketQuotesStreamLoop.JsonOptions);

    /// <summary>Resposta do EFP: a entidade em camelCase, sem as navegações.</summary>
    private Task<IActionResult> RunEfp(Func<Task<MarketEfp>> action) =>
        Run(async () =>
        {
            var efp = await action();
            var node = JsonSerializer.SerializeToNode(efp, MarketQuotesStreamLoop.JsonOptions)!.AsObject();
            node.Remove("account");
            node.Remove("instrument");
            MarketJsonDates.AsDateOnly(node, "date", "financialDueDate");
            return Json(node);
        });

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (MarketEfpPendingException e)
        {
            // A linha Processing já existe: a tela pode destacar o EFP pelo cabeçalho.
            if (e.Cause is not (BusinessException or ForbiddenException))
                logger.LogError(e.Cause, "Falha ao concluir o EFP nº {EfpId} do Mercado.", e.EfpId);
            Response.Headers[EfpIdHeader] = e.EfpId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return MarketTextResults.BadRequest(e.Message);
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
            logger.LogError(e, "Falha ao gravar o EFP do Mercado.");
            return MarketTextResults.BadRequest(
                MarketODataController<MarketEfp, int>.DescribeSaveError(e) ?? SaveFailedMessage);
        }
        catch (Exception e)
        {
            // Nenhuma rota do EFP devolve 500 cru: a exceção inteira vai para o log.
            logger.LogError(e, "Falha inesperada no EFP do Mercado.");
            return MarketTextResults.BadRequest(UnexpectedMessage);
        }
    }
}
