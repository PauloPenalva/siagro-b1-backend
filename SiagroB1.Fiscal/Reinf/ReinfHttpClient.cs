using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Reinf;

/// <summary>Cliente REST da EFD-Reinf com certificado de cliente (mTLS). Baixo volume: um HttpClient por chamada.</summary>
public sealed class ReinfHttpClient(HttpMessageHandler? handlerOverride = null) : IReinfClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public static string BaseUrl(ReinfEnvironment env) => env == ReinfEnvironment.Production
        ? "https://reinf.receita.economia.gov.br"
        : "https://pre-reinf.receita.economia.gov.br";

    public Task<ReinfBatchResult> SendAsync(string batchXml, X509Certificate2 cert, ReinfEnvironment env, CancellationToken ct) =>
        ExecuteAsync(new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(env)}/recepcao/lotes")
        {
            Content = new StringContent(batchXml, Encoding.UTF8, "application/xml"),
        }, cert, ct);

    public Task<ReinfBatchResult> QueryAsync(string protocol, X509Certificate2 cert, ReinfEnvironment env, CancellationToken ct) =>
        ExecuteAsync(new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(env)}/consulta/lotes/{Uri.EscapeDataString(protocol)}"), cert, ct);

    private async Task<ReinfBatchResult> ExecuteAsync(HttpRequestMessage req, X509Certificate2 cert, CancellationToken ct)
    {
        // Com override o handler pertence ao chamador (testes): nao descartar.
        using var ownHandler = handlerOverride is null ? NewHandler(cert) : null;
        using var http = new HttpClient(handlerOverride ?? ownHandler!, disposeHandler: false) { Timeout = RequestTimeout };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        using (req)
        {
            try
            {
                using var res = await http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                if ((int)res.StatusCode >= 400) throw new ReinfTransportException((int)res.StatusCode, body);
                try
                {
                    return ReinfResponseParser.Parse(body);
                }
                catch (Exception ex) when (ex is System.Xml.XmlException or FormatException)
                {
                    throw new ReinfUnknownOutcomeException("Retorno ilegível da EFD-Reinf: " + ex.Message, ex);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new ReinfTransportException(0, ex.Message);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // O pedido pode ter chegado: desfecho desconhecido, não é falha de conexão.
                throw new ReinfUnknownOutcomeException("Tempo esgotado: " + ex.Message, ex);
            }
        }
    }

    private static HttpClientHandler NewHandler(X509Certificate2 cert)
    {
        var h = new HttpClientHandler();
        h.ClientCertificates.Add(cert);
        return h;
    }
}
