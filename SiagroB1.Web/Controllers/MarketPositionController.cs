using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Commons.Market;
using SiagroB1.Commons.Time;
using SiagroB1.Infra.Companies;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

/// <summary>Resposta do painel: o snapshot do serviço + estado da conexão CMA (como na grade de preços).</summary>
public sealed record MarketPositionView(DateTime GeneratedAt, CmaConnectionStatus ConnectionStatus, string? ConnectionMessage,
    IReadOnlyList<MarketCommodityPosition> Commodities, MarketUsdPosition Usd, MarketResultSummary Results,
    IReadOnlyList<MarketLimitAlert> Alerts, IReadOnlyList<MarketIgnoredContract> Ignored);

/// <summary>
/// Módulo Mercado: painel de posição (REST e SSE) e as listas que formam cada célula. Leitura livre para autenticados.
/// JSON camelCase, enums camelCase, datas yyyy-MM-dd (DateOnly), carimbos UTC com Z.
/// </summary>
[Authorize]
[MarketModuleEnabled]
public class MarketPositionController(
    MarketPositionService position, MarketQuotesSseWriter sse, ICompanyContext companyContext, TimeProvider time,
    LiveQuoteStore store) : ControllerBase
{
    /// <summary>Uma emissão do SSE a cada 2 s no máximo: cada uma recalcula a posição.</summary>
    public static readonly TimeSpan LiveMinInterval = TimeSpan.FromSeconds(2);

    [HttpGet("market/position")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Json(WithConnection(await position.GetSnapshotAsync(Today(time), cancellationToken), store.Get(companyContext.CompanyCode)));

    [HttpGet("market/position/live")]
    public Task Live(CancellationToken cancellationToken) =>
        // Escopo de DI novo por emissão (o DbContext da requisição não atravessa o stream longo).
        sse.RunAsync(HttpContext, companyContext, "a posição", async (services, snapshot, ct) =>
            WithConnection(
                await services.GetRequiredService<MarketPositionService>()
                    .GetSnapshotAsync(Today(services.GetRequiredService<TimeProvider>()), ct),
                snapshot),
            cancellationToken, LiveMinInterval);

    [HttpGet("market/position/contracts")]
    public async Task<IActionResult> Contracts(string? commodity, int? year, int? month, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commodity))
            return MarketTextResults.BadRequest("Informe a commodity.");
        // Ano e mês juntos = um vencimento; os dois ausentes = a linha de total da commodity (todos os vencimentos).
        if ((year is null) != (month is null) || month is < 1 or > 12)
            return MarketTextResults.BadRequest("Informe o ano e o mês do vencimento, ou nenhum dos dois para o total.");
        return Json(await position.GetBucketContractsAsync(commodity, year, month, cancellationToken));
    }

    [HttpGet("market/position/usd-contracts")]
    public async Task<IActionResult> UsdContracts(CancellationToken cancellationToken) =>
        Json(await position.GetUsdContractsAsync(cancellationToken));

    public static MarketPositionView WithConnection(MarketPositionSnapshot s, LiveQuoteSnapshot connection) =>
        new(DateTime.SpecifyKind(s.GeneratedAt, DateTimeKind.Utc), connection.Status, connection.Message,
            s.Commodities, s.Usd, s.Results, s.Alerts, s.Ignored);

    private static JsonResult Json(object value) => new(value, MarketQuotesStreamLoop.JsonOptions);

    private static DateOnly Today(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), SaoPauloTime.Zone).DateTime);
}
