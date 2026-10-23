using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Transport;

/// <summary>Resultado do login na AT&amp;M: o token (Bearer) ou a descrição do erro.</summary>
public record AverbaLoginResult(bool Success, string? Bearer, string? Error);

/// <summary>Um seguro averbado pela AT&amp;M.</summary>
public record AverbaInsurance(string? EndorsementNumber, string? InsurerTaxId, string? InsurerName, string? PolicyNumber, string? Amount, string? Branch, string? MovementType);

/// <summary>Resultado do envio do CT-e à AT&amp;M. Sucesso = protocolo de averbação preenchido.</summary>
public record AverbaCteResult(bool Success, string? Protocol, string? AverbadoAt, IReadOnlyList<AverbaInsurance> Insurances, string? Error);

/// <summary>Cliente da AT&amp;M (averba.com.br): login e envio do CT-e (<c>/rest/CTE</c>).</summary>
public interface IAverbaClient
{
    Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct);

    Task<AverbaCteResult> SendCteAsync(NfeEnvironment environment, string bearer, string xml, CancellationToken ct);
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
            // Sem Accept a AT&M recusa: "Header Accept nao informado".
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/rest/Auth")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new Dictionary<string, string> { ["usuario"] = user, ["senha"] = password, ["codigoatm"] = code }),
                    System.Text.Encoding.UTF8),
            };
            // A AT&M recusa "application/json; charset=utf-8": o Content-Type tem de ser exatamente application/json.
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, ct);

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

    public async Task<AverbaCteResult> SendCteAsync(NfeEnvironment environment, string bearer, string xml, CancellationToken ct)
    {
        var baseUrl = configuration[environment == NfeEnvironment.Production ? "Averba:ProductionUrl" : "Averba:HomologationUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return CteFailure(MissingUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/rest/CTE")
            {
                Content = new StringContent(xml, System.Text.Encoding.UTF8),
            };
            // Como no login, a AT&M exige o Content-Type exato, sem charset.
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

            using var response = await http.SendAsync(request, ct);

            // Erros vêm com HTTP 400 e o motivo no corpo: lê o corpo antes do status.
            var text = await response.Content.ReadAsStringAsync(ct);
            CteResponse? body = null;
            try
            {
                body = string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<CteResponse>(text);
            }
            catch (JsonException)
            {
                // Corpo que não é JSON (página de erro do servidor): cai nas mensagens abaixo.
            }

            var protocol = body?.Averbado?.Protocol;
            if (!string.IsNullOrWhiteSpace(protocol))
            {
                var insurances = (body!.Averbado!.Insurances ?? [])
                    .Select(i => new AverbaInsurance(i.EndorsementNumber, i.InsurerTaxId, i.InsurerName, i.PolicyNumber, i.Amount, i.Branch, i.MovementType))
                    .ToList();
                return new AverbaCteResult(true, protocol, body.Averbado.AverbadoAt, insurances, null);
            }

            var errors = (body?.Errors ?? [])
                .Select(e => e.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();
            if (errors.Count > 0)
                return CteFailure(string.Join("; ", errors));

            if (!response.IsSuccessStatusCode)
                return CteFailure($"A AT&M respondeu HTTP {(int)response.StatusCode}.");

            return CteFailure("A AT&M não devolveu o protocolo de averbação.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return CteFailure($"Não foi possível conectar à AT&M: {e.Message}");
        }
    }

    private static AverbaCteResult CteFailure(string error) => new(false, null, null, [], error);

    /// <summary>Lê número/booleano JSON como texto (o Jackson do EfisCloud aceita; sem isso um sucesso viraria falha e a averbação seria duplicada).</summary>
    private sealed class LenientStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException("Valor inesperado no lugar de um texto."),
        };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    private sealed class CteResponse
    {
        [JsonPropertyName("Averbado")] public CteAverbado? Averbado { get; set; }
        [JsonPropertyName("Erros")] public List<CteError>? Errors { get; set; }
    }

    private sealed class CteAverbado
    {
        // Sem JsonProperty no DTO Java da AT&M: este campo é lower-camel, os demais PascalCase.
        [JsonPropertyName("dhAverbacao")] [JsonConverter(typeof(LenientStringConverter))] public string? AverbadoAt { get; set; }
        [JsonPropertyName("Protocolo")] [JsonConverter(typeof(LenientStringConverter))] public string? Protocol { get; set; }
        [JsonPropertyName("DadosSeguro")] public List<CteInsurance>? Insurances { get; set; }
    }

    private sealed class CteInsurance
    {
        [JsonPropertyName("NumeroAverbacao")] [JsonConverter(typeof(LenientStringConverter))] public string? EndorsementNumber { get; set; }
        [JsonPropertyName("CNPJSeguradora")] [JsonConverter(typeof(LenientStringConverter))] public string? InsurerTaxId { get; set; }
        [JsonPropertyName("NomeSeguradora")] [JsonConverter(typeof(LenientStringConverter))] public string? InsurerName { get; set; }
        [JsonPropertyName("NumApolice")] [JsonConverter(typeof(LenientStringConverter))] public string? PolicyNumber { get; set; }
        [JsonPropertyName("ValorAverbado")] [JsonConverter(typeof(LenientStringConverter))] public string? Amount { get; set; }
        [JsonPropertyName("RamoAverbado")] [JsonConverter(typeof(LenientStringConverter))] public string? Branch { get; set; }
        [JsonPropertyName("TpMov")] [JsonConverter(typeof(LenientStringConverter))] public string? MovementType { get; set; }
    }

    private sealed class CteError
    {
        [JsonPropertyName("Descricao")] [JsonConverter(typeof(LenientStringConverter))] public string? Description { get; set; }
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
