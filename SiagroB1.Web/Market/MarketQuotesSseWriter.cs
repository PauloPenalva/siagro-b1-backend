using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using SiagroB1.Application.Services.Market;
using SiagroB1.Commons.Market;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Web.Market;

/// <summary>
/// Laço do SSE de cotações. O banco é a fonte: a cada mudança de versão do store o quadro é relido
/// num escopo novo (a requisição é longa demais para reaproveitar o DbContext dela).
/// </summary>
public sealed class MarketQuotesSseWriter(LiveQuoteStore store, IServiceScopeFactory scopeFactory)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task RunAsync(HttpContext http, ICompanyContext requestCompany, CancellationToken cancellationToken)
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
        var lastVersion = -1L;
        var lastWrite = DateTime.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var snapshot = store.Get(companyCode);

                if (snapshot.Version != lastVersion)
                {
                    lastVersion = snapshot.Version;
                    await EmitAsync(response, snapshot, company, companyCode, cancellationToken);
                    lastWrite = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastWrite >= HeartbeatInterval)
                {
                    await response.WriteAsync(": ping\n\n", cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                    lastWrite = DateTime.UtcNow;
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Navegador fechou a conexão: encerramento normal.
        }
    }

    private async Task EmitAsync(
        HttpResponse response, LiveQuoteSnapshot snapshot, CompanyInfo? company, string companyCode, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketQuoteView> quotes;

        using (CompanyAmbient.Use(companyCode))
        using (var scope = scopeFactory.CreateScope())
        {
            if (company is not null)
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);

            quotes = await scope.ServiceProvider.GetRequiredService<MarketQuoteService>().GetBoardAsync(cancellationToken);
        }

        var payload = new { snapshot.Version, snapshot.Status, snapshot.Message, Quotes = quotes };
        await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
