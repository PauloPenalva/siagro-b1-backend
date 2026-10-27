namespace SiagroB1.Installer.Core.Services;

public static class EndpointProbe
{
    /// <summary>2xx a 4xx conta como "respondendo" (401/404 são normais sem login), igual aos scripts antigos.</summary>
    public static async Task<(bool Responding, int? Status)> ProbeAsync(Uri url, TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            using var response = await client.GetAsync(url, cts.Token);
            var status = (int)response.StatusCode;
            return (status is >= 200 and < 500, status);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return (false, null);
        }
    }
}
