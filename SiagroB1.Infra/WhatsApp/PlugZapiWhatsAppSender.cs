using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Interfaces.Notifications;

namespace SiagroB1.Infra.WhatsApp;

/// <summary>
/// Corpo do <c>send-text</c> do PlugZapi.
///
/// As propriedades são PascalCase mas trafegam em camelCase (<c>phone</c>, <c>message</c>,
/// <c>delayMessage</c>), como o provedor exige: <c>JsonContent.Create</c> e
/// <c>ReadFromJsonAsync</c> usam <c>JsonSerializerDefaults.Web</c> quando nenhuma opção é
/// passada. Não passe um <c>JsonSerializerOptions</c> "padrão" aqui — isso voltaria para
/// PascalCase e o PlugZapi recusaria a requisição.
/// </summary>
/// <param name="DelayMessage">Atraso antes de enviar, em SEGUNDOS (faixa aceita: 1 a 15).</param>
public record PlugZapiSendTextRequest(
    string Phone,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DelayMessage);

/// <summary>Resposta de sucesso do <c>send-text</c>: <c>{"zaapId": "...", "messageId": "..."}</c>.</summary>
public record PlugZapiSendTextResponse(string? ZaapId, string? MessageId);

/// <summary>
/// Envio de WhatsApp via PlugZapi.
///
/// <para>
/// Duas particularidades do provedor moldam esta classe. Primeira: a instância e o token vão no
/// PATH da URL, não em cabeçalho — por isso a rota é montada a cada requisição e NADA que
/// carregue a URL pode chegar ao resultado. O <c>Error</c> devolvido é gravado no log e exibido
/// na tela de administração; deixar a URL vazar ali entregaria o token a qualquer administrador
/// e a qualquer arquivo de log.
/// </para>
/// <para>
/// Segunda: o sucesso é HTTP 200 (e não 201, como em outros gateways de WhatsApp).
/// </para>
///
/// Nunca lança por falha do provedor: quem decide retentar é o job, olhando
/// <see cref="WhatsAppSendResult.Transient"/>.
/// </summary>
public class PlugZapiWhatsAppSender(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PlugZapiWhatsAppSender> logger) : IWhatsAppSender
{
    private const int MinDelaySeconds = 1;
    private const int MaxDelaySeconds = 15;

    public async Task<WhatsAppSendResult> SendTextAsync(
        string phoneE164, string text, CancellationToken ct = default)
    {
        var instanceId = configuration["Notifications:WhatsApp:InstanceId"];
        var token = configuration["Notifications:WhatsApp:Token"];

        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(token))
            return WhatsAppSendResult.Permanent(null, "Instância ou token do PlugZapi não configurados.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"instances/{instanceId}/token/{token}/send-text")
        {
            Content = JsonContent.Create(new PlugZapiSendTextRequest(phoneE164, text, DelaySeconds())),
        };

        var clientToken = configuration["Notifications:WhatsApp:ClientToken"];

        // Enviado só quando configurado: a doc pública não mostra este cabeçalho nos exemplos,
        // mas ele é ativável no painel da conta e passa a ser obrigatório quando ativo.
        if (!string.IsNullOrWhiteSpace(clientToken))
            request.Headers.Add("Client-Token", clientToken);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);

            return response.IsSuccessStatusCode
                ? WhatsAppSendResult.Ok((int)response.StatusCode, await ReadMessageIdAsync(response, ct))
                : await FailureAsync(response, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout do HttpClient chega como cancelamento sem o token ter sido cancelado.
            logger.LogWarning("Timeout ao enviar WhatsApp para {Phone}.", phoneE164);
            return WhatsAppSendResult.Retryable(null, "Timeout ao contatar o provedor de WhatsApp.");
        }
        catch (HttpRequestException exception)
        {
            // A mensagem da exceção pode conter a URL (e portanto o token) — descartada de
            // propósito; o LogError abaixo também recebe só a exceção, nunca a rota montada.
            logger.LogError(exception, "Falha de rede ao enviar WhatsApp para {Phone}.", phoneE164);
            return WhatsAppSendResult.Retryable(null, "Falha de rede ao contatar o provedor de WhatsApp.");
        }
    }

    private async Task<WhatsAppSendResult> FailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;
        var body = Truncate(await response.Content.ReadAsStringAsync(ct));
        var error = $"PlugZapi respondeu {statusCode}. {body}".TrimEnd();

        // 429 e 5xx podem melhorar sozinhos; o resto não. Instância desconectada e número
        // inválido — as falhas mais comuns — vêm como 4xx e não devem ser retentadas: só
        // queimariam quota e adiariam a descoberta do problema real.
        return response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.RequestTimeout
                || statusCode >= 500
            ? WhatsAppSendResult.Retryable(statusCode, error)
            : WhatsAppSendResult.Permanent(statusCode, error);
    }

    private static async Task<string?> ReadMessageIdAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<PlugZapiSendTextResponse>(ct))?.MessageId;
        }
        catch (Exception)
        {
            // A mensagem foi aceita; não conseguir ler o identificador não torna o envio falho.
            return null;
        }
    }

    /// <summary>
    /// Atraso entre mensagens, para não disparar em rajada — o maior risco operacional de um
    /// provedor não-oficial é o banimento do número da empresa. Fora da faixa aceita pelo
    /// PlugZapi, o parâmetro é omitido em vez de fazer a requisição inteira ser recusada.
    /// </summary>
    private int? DelaySeconds()
    {
        var configured = configuration.GetValue<int?>("Notifications:WhatsApp:DelayMessageSeconds");

        return configured is >= MinDelaySeconds and <= MaxDelaySeconds ? configured : null;
    }

    private static string Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" :
        value.Length <= 300 ? value : value[..300];
}
