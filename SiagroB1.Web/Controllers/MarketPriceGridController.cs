using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Services.Market.Pricing;
using SiagroB1.Commons.Market;
using SiagroB1.Commons.Time;
using SiagroB1.Infra.Companies;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

/// <summary>Módulo Mercado: grade "Ordem de Compra" (preço por cidade), pontual e ao vivo (SSE). Leitura livre para autenticados.</summary>
[ApiController]
[Authorize]
[MarketModuleEnabled]
[Route("market")]
public class MarketPriceGridController(
    MarketPriceGridService grid, MarketQuotesSseWriter sse, ICompanyContext companyContext, TimeProvider time,
    LiveQuoteStore store) : ControllerBase
{
    [HttpGet("price-grid")]
    public async Task<IActionResult> Get(string? harvest, string? city, CancellationToken cancellationToken)
    {
        if (Validate(harvest, city) is { } error)
            return error;

        var result = WithConnection(
            await grid.GetGridAsync(harvest!.Trim(), city!.Trim(), Today(time), cancellationToken),
            store.Get(companyContext.CompanyCode));
        // Mesmas opções do SSE: camelCase, enums como texto e datas yyyy-MM-dd.
        return new JsonResult(result, MarketQuotesStreamLoop.JsonOptions);
    }

    [HttpGet("price-grid/live")]
    public Task Live(string? harvest, string? city, CancellationToken cancellationToken)
    {
        if (Validate(harvest, city) is { } error)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "text/plain; charset=utf-8";
            return Response.WriteAsync((string)((ContentResult)error).Content!, cancellationToken);
        }

        var harvestCode = harvest!.Trim();
        var cityCode = city!.Trim();
        return sse.RunAsync(HttpContext, companyContext, "a grade de preços", async (services, snapshot, ct) =>
            WithConnection(
                await services.GetRequiredService<MarketPriceGridService>()
                    .GetGridAsync(harvestCode, cityCode, Today(services.GetRequiredService<TimeProvider>()), ct),
                snapshot),
            cancellationToken);
    }

    /// <summary>Estado da conexão CMA da empresa, o mesmo que o SSE do terminal manda ("Ao vivo"/"Sem CMA").</summary>
    public static MarketPriceGrid WithConnection(MarketPriceGrid grid, LiveQuoteSnapshot snapshot) =>
        grid with { ConnectionStatus = snapshot.Status, ConnectionMessage = snapshot.Message };

    private static DateOnly Today(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), SaoPauloTime.Zone).DateTime);

    private static ContentResult? Validate(string? harvest, string? city)
    {
        if (string.IsNullOrWhiteSpace(harvest))
            return Text("Informe a safra.");
        if (string.IsNullOrWhiteSpace(city))
            return Text("Informe a cidade.");
        return null;
    }

    private static ContentResult Text(string message) => MarketTextResults.BadRequest(message);
}
