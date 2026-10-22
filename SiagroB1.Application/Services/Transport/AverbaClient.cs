using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Transport;

/// <summary>Resultado do login na AT&amp;M: o token (Bearer) ou a descrição do erro.</summary>
public record AverbaLoginResult(bool Success, string? Bearer, string? Error);

/// <summary>
/// Cliente da AT&amp;M (averba.com.br). Por ora só o login, usado no "Testar conexão"; o envio do
/// CT-e (<c>/rest/CTE</c>) entra com a emissão do CT-e.
/// </summary>
public interface IAverbaClient
{
    Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct);
}

public class AverbaClient(HttpClient http, IConfiguration configuration) : IAverbaClient
{
    private const string MissingUrl = "Configure Averba:ProductionUrl/HomologationUrl no servidor.";

    public async Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct)
    {
        var baseUrl = configuration[environment == NfeEnvironment.Production ? "Averba:ProductionUrl" : "Averba:HomologationUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new AverbaLoginResult(false, null, MissingUrl);

        try
        {
            // Nomes dos campos como a AT&M espera (os mesmos do EfisCloud).
            using var response = await http.PostAsJsonAsync(
                $"{baseUrl.TrimEnd('/')}/rest/Auth",
                new Dictionary<string, string> { ["usuario"] = user, ["senha"] = password, ["codigoatm"] = code },
                ct);

            // A AT&M recusa credencial inválida com HTTP 400 e o motivo no corpo: lê o corpo antes do status.
            var body = await ReadBodyAsync(response, ct);
            var error = body?.Errors?.Error?.FirstOrDefault()?.Description;

            if (!string.IsNullOrWhiteSpace(error))
                return new AverbaLoginResult(false, null, error);

            if (!response.IsSuccessStatusCode)
                return new AverbaLoginResult(false, null, $"A AT&M respondeu HTTP {(int)response.StatusCode}.");

            return string.IsNullOrWhiteSpace(body?.Bearer)
                ? new AverbaLoginResult(false, null, "A AT&M não devolveu o token de acesso.")
                : new AverbaLoginResult(true, body.Bearer, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new AverbaLoginResult(false, null, $"Não foi possível conectar à AT&M: {e.Message}");
        }
    }

    /// <summary>Corpo como JSON da AT&amp;M, ou null quando não é JSON (ex.: página de erro do servidor).</summary>
    private static async Task<LoginResponse?> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<LoginResponse>(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("Bearer")] public string? Bearer { get; set; }
        [JsonPropertyName("Erros")] public ErrorList? Errors { get; set; }
    }

    private sealed class ErrorList
    {
        [JsonPropertyName("Erro")] public List<ErrorItem>? Error { get; set; }
    }

    private sealed class ErrorItem
    {
        [JsonPropertyName("Descricao")] public string? Description { get; set; }
    }
}
