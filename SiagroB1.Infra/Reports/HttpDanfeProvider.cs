using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Infra.Reports;

/// <summary>
/// Busca o DANFE na rota interna do SiagroB1.Reports (<c>internal/danfe/{sales|purchase}/{key}</c>), fora de
/// <c>/reports</c> para o Gateway nunca a expor. Autentica pela chave <c>Reports:InternalApiKey</c> e diz a empresa
/// no cabeçalho <c>X-Company</c> — o job não tem cookie de sessão.
/// </summary>
public class HttpDanfeProvider(HttpClient http, IConfiguration configuration) : IDanfeProvider
{
    public async Task<byte[]> GetPdfAsync(NfeEmailDocumentType type, Guid key, string companyCode, CancellationToken ct = default)
    {
        var baseUrl = configuration["Reports:InternalBaseUrl"];
        var apiKey = configuration["Reports:InternalApiKey"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Reports:InternalBaseUrl/Reports:InternalApiKey não configurados.");

        var segment = type == NfeEmailDocumentType.Purchase ? "purchase" : "sales";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/internal/danfe/{segment}/{key}");
        request.Headers.Add("X-Internal-Key", apiKey);
        request.Headers.Add("X-Company", companyCode);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Reports respondeu {(int)response.StatusCode} ao gerar o DANFE.");

        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
