using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Infra.ESignature.D4Sign;

/// <summary>
/// Assinatura eletrônica pelo D4Sign. HttpClient tipado; BaseAddress e credenciais vêm da seção
/// <c>Signature:D4Sign</c>, lidas a cada chamada (sem IOptions, como o resto do solution).
///
/// Credenciais: no upload vão em HEADER; nas demais chamadas vão na QUERY. É assim que a API do
/// D4Sign funciona e é o que a Tagui faz hoje.
///
/// Nenhum método lança por falha do provedor. Mensagens são truncadas em 300 caracteres e a
/// mensagem de HttpRequestException é descartada: ela costuma carregar a URL, e a URL carrega o
/// token.
/// </summary>
public sealed class D4SignProvider(
    HttpClient http,
    IConfiguration configuration,
    ILogger<D4SignProvider> logger) : IESignatureProvider
{
    public const string TokenApiKey = "Signature:D4Sign:TokenApi";
    public const string CryptKeyKey = "Signature:D4Sign:CryptKey";
    public const string SafeIdKey = "Signature:D4Sign:SafeId";

    private const int MaxErrorLength = 300;
    private const string NetworkFailure = "Não foi possível falar com o D4Sign.";

    public string Name => "D4Sign";

    private string Token => configuration[TokenApiKey] ?? "";
    private string Crypt => configuration[CryptKeyKey] ?? "";
    private string SafeId => configuration[SafeIdKey] ?? "";

    /// <summary>Query com as credenciais — usada em tudo, menos no upload.</summary>
    private string Credentials => $"?tokenAPI={Uri.EscapeDataString(Token)}&cryptKey={Uri.EscapeDataString(Crypt)}";

    public async Task<ESignatureSendResult> SendAsync(ESignatureSendRequest request, CancellationToken ct = default)
    {
        string? uuid = null;
        try
        {
            // 1. upload — credenciais em header, multipart
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(request.PdfBytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", request.FileName);

            using var upload = new HttpRequestMessage(HttpMethod.Post, $"documents/{SafeId}/upload") { Content = form };
            upload.Headers.Add("tokenAPI", Token);
            upload.Headers.Add("cryptKey", Crypt);

            var uploadResponse = await http.SendAsync(upload, ct);
            if (!uploadResponse.IsSuccessStatusCode)
                return ESignatureSendResult.Fail(await DescribeAsync(uploadResponse, ct), IsTransient(uploadResponse.StatusCode));

            uuid = (await ReadAsync<D4SignUploadResponse>(uploadResponse, ct))?.Uuid;
            if (string.IsNullOrWhiteSpace(uuid))
                return ESignatureSendResult.Fail("O D4Sign não devolveu o identificador do documento.", false);

            // 2. signatários
            var signers = request.Signers
                .OrderBy(s => s.Order)
                .Select(s => new D4SignSigner(s.Email, ActOf(s.Role), "0", "0", "0", "0", "0", "email", "0"))
                .ToList();

            var createList = await http.PostAsJsonAsync($"documents/{uuid}/createlist{Credentials}",
                new D4SignCreateListRequest(signers), ct);
            if (!createList.IsSuccessStatusCode)
                return await AbortAsync(uuid, createList, ct);

            // 3. webhook — opcional: sem URL pública configurada, só a reconciliação atualiza
            if (!string.IsNullOrWhiteSpace(request.WebhookUrl))
            {
                var webhook = await http.PostAsJsonAsync($"documents/{uuid}/webhooks{Credentials}",
                    new D4SignWebhookRequest(request.WebhookUrl), ct);
                if (!webhook.IsSuccessStatusCode)
                    return await AbortAsync(uuid, webhook, ct);
            }

            // 4. enviar — workflow "0" = sem ordem obrigatória
            var send = await http.PostAsJsonAsync($"documents/{uuid}/sendtosigner{Credentials}",
                new D4SignSendToSignerRequest(request.Message, "0", "0", Token), ct);
            if (!send.IsSuccessStatusCode)
                return await AbortAsync(uuid, send, ct);

            return ESignatureSendResult.Ok(uuid);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Falha de rede ao enviar minuta ao D4Sign. Documento: {Uuid}", uuid ?? "(nenhum)");
            if (uuid is not null) await TryCancelAsync(uuid, ct);
            return ESignatureSendResult.Fail(NetworkFailure, true);
        }
    }

    public async Task<ESignatureResult> CancelAsync(string externalDocumentId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsync($"documents/{externalDocumentId}/cancel{Credentials}", null, ct);
            return response.IsSuccessStatusCode
                ? ESignatureResult.Ok
                : ESignatureResult.Fail(await DescribeAsync(response, ct), IsTransient(response.StatusCode));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return ESignatureResult.Fail(NetworkFailure, true);
        }
    }

    public async Task<ESignatureDocumentState> GetStateAsync(string externalDocumentId, CancellationToken ct = default)
    {
        try
        {
            var documentResponse = await http.GetAsync($"documents/{externalDocumentId}{Credentials}", ct);
            if (!documentResponse.IsSuccessStatusCode)
                return ESignatureDocumentState.Unknown(await DescribeAsync(documentResponse, ct));

            // O endpoint devolve uma LISTA com um documento.
            var documents = await ReadAsync<List<D4SignDocument>>(documentResponse, ct);
            var status = StatusOf(documents?.FirstOrDefault()?.StatusId);

            var listResponse = await http.GetAsync($"documents/{externalDocumentId}/list{Credentials}", ct);
            if (!listResponse.IsSuccessStatusCode)
                return new ESignatureDocumentState(status, []);

            var list = await ReadAsync<D4SignListResponse>(listResponse, ct);
            var signers = (list?.List ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s.Email))
                .Select(s => new ESignatureSignerState(s.Email!, s.Signed == "1", ParseDate(s.SignedDate), Truncate(s.Message)))
                .ToList();

            return new ESignatureDocumentState(status, signers);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return ESignatureDocumentState.Unknown(NetworkFailure);
        }
    }

    public async Task<byte[]?> DownloadSignedAsync(string externalDocumentId, CancellationToken ct = default)
    {
        try
        {
            var post = await http.PostAsJsonAsync($"documents/{externalDocumentId}/download{Credentials}",
                new D4SignDownloadRequest("PDF", "pt"), ct);
            if (!post.IsSuccessStatusCode) return null;

            var url = (await ReadAsync<D4SignDownloadResponse>(post, ct))?.Url;
            if (string.IsNullOrWhiteSpace(url)) return null;

            // A URL do download é absoluta e não leva credencial.
            var file = await http.GetAsync(url, ct);
            return file.IsSuccessStatusCode ? await file.Content.ReadAsByteArrayAsync(ct) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Falha de rede ao baixar PDF assinado do documento {Uuid}", externalDocumentId);
            return null;
        }
    }

    /// <summary>Cancela o documento órfão e devolve a falha do passo que quebrou.</summary>
    private async Task<ESignatureSendResult> AbortAsync(string uuid, HttpResponseMessage failed, CancellationToken ct)
    {
        var message = await DescribeAsync(failed, ct);
        var transient = IsTransient(failed.StatusCode);
        logger.LogWarning("Envio ao D4Sign falhou depois do upload; cancelando documento {Uuid}", uuid);
        await TryCancelAsync(uuid, ct);
        return ESignatureSendResult.Fail(message, transient);
    }

    private async Task TryCancelAsync(string uuid, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsync($"documents/{uuid}/cancel{Credentials}", null, ct);

            // Cancelamento rejeitado (400/409/500...) é tão órfão quanto uma exceção de rede —
            // sem este log, ninguém saberia que o documento ficou preso no cofre do D4Sign.
            // O status vai junto porque é o que distingue os dois ramos: "o D4Sign recusou o
            // cancelamento" pede intervenção no cofre; "não falei com o D4Sign" pode ter
            // cancelado assim mesmo. Só a URL é que nunca pode ir ao log — ela carrega o token.
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Documento {Uuid} ficou órfão no cofre do D4Sign: cancelamento recusado com {Status}",
                    uuid, (int)response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Documento {Uuid} ficou órfão no cofre do D4Sign: falha de rede no cancelamento", uuid);
        }
    }

    /// <summary>
    /// Corpo em JSON, ou <c>default</c> quando não dá para ler. NotSupportedException entra no
    /// filtro junto com JsonException: <c>ReadFromJsonAsync</c> lança NotSupportedException — que
    /// não é JsonException — quando o Content-Type não é compatível com JSON. Um 200 devolvendo
    /// página HTML de manutenção, de WAF ou de portal cativo é o caso real, e sem isto a exceção
    /// escaparia dos filtros <c>HttpRequestException or TaskCanceledException</c> de todos os
    /// métodos públicos, quebrando a garantia de que este provider nunca lança.
    /// </summary>
    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { return default; }
    }

    /// <summary>Corpo da resposta de erro, truncado. Nunca inclui a URL — ela carrega o token.</summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try { body = await response.Content.ReadAsStringAsync(ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { body = ""; }

        var text = string.IsNullOrWhiteSpace(body) ? "sem detalhe" : body.Trim();
        return Truncate($"D4Sign respondeu {(int)response.StatusCode}: {text}")!;
    }

    private static string? Truncate(string? text) =>
        text is null ? null : text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>4 = finalizado, 6 = cancelado; o resto é pendente.</summary>
    private static ESignatureDocumentStatus StatusOf(string? statusId) => statusId switch
    {
        "4" => ESignatureDocumentStatus.Finished,
        "6" => ESignatureDocumentStatus.Canceled,
        null or "" => ESignatureDocumentStatus.Unknown,
        _ => ESignatureDocumentStatus.Pending,
    };

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    /// <summary>
    /// O valor do enum já é o código do ato no D4Sign; a conversão é explícita para que uma
    /// renumeração futura do enum não mude, em silêncio, o que o D4Sign recebe.
    /// </summary>
    private static string ActOf(SignatoryRole role) => role switch
    {
        SignatoryRole.Sign => "1",
        SignatoryRole.Approve => "2",
        SignatoryRole.Acknowledge => "3",
        SignatoryRole.SignAsParty => "4",
        SignatoryRole.SignAsWitness => "5",
        SignatoryRole.SignAsIntervening => "6",
        SignatoryRole.AcknowledgeReceipt => "7",
        SignatoryRole.SignAsIssuerEndorserGuarantor => "8",
        SignatoryRole.SignAsIssuerEndorserGuarantorSurety => "9",
        SignatoryRole.SignAsSurety => "10",
        SignatoryRole.SignAsPartyAndSurety => "11",
        SignatoryRole.SignAsJointDebtor => "12",
        SignatoryRole.SignAsPartyAndJointDebtor => "13",
        _ => "1",
    };
}
