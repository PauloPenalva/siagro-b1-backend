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
    public Task RunAsync(HttpContext http, ICompanyContext requestCompany, CancellationToken cancellationToken)
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

        return new MarketQuotesStreamLoop(store, loopLogger)
            .RunAsync(companyCode, response.Body, ct => ReadBoardAsync(company, companyCode, ct), cancellationToken);
    }

    private async Task<IReadOnlyList<MarketQuoteView>> ReadBoardAsync(CompanyInfo? company, string companyCode, CancellationToken cancellationToken)
    {
        using var _ = CompanyAmbient.Use(companyCode);
        using var scope = scopeFactory.CreateScope();
        if (company is not null)
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);

        return await scope.ServiceProvider.GetRequiredService<MarketQuoteService>().GetBoardAsync(cancellationToken);
    }
}
