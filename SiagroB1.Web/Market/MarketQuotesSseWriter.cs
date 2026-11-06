using Microsoft.AspNetCore.Http.Features;
using SiagroB1.Application.Services.Market;
using SiagroB1.Commons.Market;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Web.Market;

/// <summary>
/// Ligação HTTP do SSE de cotações: cabeçalhos e escopo de DI por leitura (a requisição é longa
/// demais para reaproveitar o DbContext dela). O laço em si está em <see cref="MarketQuotesStreamLoop"/>.
/// </summary>
public sealed class MarketQuotesSseWriter(
    LiveQuoteStore store, IServiceScopeFactory scopeFactory, ILogger<MarketQuotesStreamLoop> loopLogger)
{
    public Task RunAsync(HttpContext http, ICompanyContext requestCompany, CancellationToken cancellationToken) =>
        RunAsync(http, requestCompany, "o quadro de cotações", async (services, snapshot, ct) =>
        {
            var quotes = await services.GetRequiredService<MarketQuoteService>().GetBoardAsync(ct);
            return new { snapshot.Version, snapshot.Status, snapshot.Message, Quotes = quotes };
        }, cancellationToken);

    /// <summary>
    /// SSE genérico do Mercado: cabeçalhos, escopo de DI novo por emissão (com a empresa da requisição) e laço
    /// de versão/heartbeat. <paramref name="buildPayload"/> roda dentro do escopo e devolve o objeto do evento.
    /// </summary>
    public Task RunAsync(
        HttpContext http, ICompanyContext requestCompany, string streamName,
        Func<IServiceProvider, LiveQuoteSnapshot, CancellationToken, Task<object>> buildPayload,
        CancellationToken cancellationToken)
    {
        var response = http.Response;
        response.Headers.ContentType = "text/event-stream";
        // Mesmos cabeçalhos do ScalesController.Live: sem no-transform/X-Accel-Buffering, compressão e
        // proxies seguram os chunks até encher o buffer.
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var company = requestCompany.Current;
        var companyCode = requestCompany.CompanyCode;

        return new MarketLiveStreamLoop(store, loopLogger)
            .RunAsync(companyCode, response.Body, streamName,
                (snapshot, ct) => BuildInScopeAsync(company, companyCode, snapshot, buildPayload, ct), cancellationToken);
    }

    private async Task<object> BuildInScopeAsync(
        CompanyInfo? company, string companyCode, LiveQuoteSnapshot snapshot,
        Func<IServiceProvider, LiveQuoteSnapshot, CancellationToken, Task<object>> buildPayload, CancellationToken cancellationToken)
    {
        using var _ = CompanyAmbient.Use(companyCode);
        using var scope = scopeFactory.CreateScope();
        if (company is not null)
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);

        return await buildPayload(scope.ServiceProvider, snapshot, cancellationToken);
    }
}
