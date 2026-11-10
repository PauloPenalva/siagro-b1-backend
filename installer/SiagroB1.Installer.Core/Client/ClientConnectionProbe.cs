namespace SiagroB1.Installer.Core.Client;

public sealed record ClientProbeResult(bool Ok, string Message);

/// <summary>
/// Testa o caminho até o servidor sem abrir WebSocket, como o "Diagnóstico rápido" do README do Client:
/// um GET comum nunca chega a 101, mas o código devolvido isola a camada com problema.
/// </summary>
public static class ClientConnectionProbe
{
    public static async Task<ClientProbeResult> ProbeAsync(
        string webSocketUrl, string scaleCode, string clientKey, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(webSocketUrl.Trim(), UriKind.Absolute, out var ws) || ws.Scheme is not ("ws" or "wss"))
        {
            return new ClientProbeResult(false, "URL inválida: use ws:// ou wss://.");
        }

        var http = new UriBuilder(ws) { Scheme = ws.Scheme == "wss" ? "https" : "http" };
        http.Query = "truckScaleId=" + Uri.EscapeDataString(scaleCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, http.Uri);
        request.Headers.TryAddWithoutValidation("X-Scale-Client-Key", clientKey);

        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            using var response = await client.SendAsync(request, cts.Token);
            return Interpret((int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return new ClientProbeResult(false,
                "Não foi possível alcançar o servidor. Confira o endereço, a porta e o firewall.");
        }
    }

    public static ClientProbeResult Interpret(int status) => status switch
    {
        400 => new ClientProbeResult(true, "Servidor alcançado: a rota e a chave estão corretas."),
        401 => new ClientProbeResult(false, "O servidor recusou a chave (401). Ela deve ser idêntica à TruckScale:ClientKey do servidor."),
        404 => new ClientProbeResult(false,
            "Rota não encontrada (404). Se a URL aponta para o Gateway, falta a rota truck-scale-ws-route no appsettings dele."),
        _ => new ClientProbeResult(false, $"Resposta inesperada do servidor (HTTP {status}).")
    };
}
