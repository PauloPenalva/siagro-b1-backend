namespace SiagroB1.Commons.Scales;

/// <summary>
/// Mantém a conexão com o indicador e devolve cada peso lido pelo callback. Reconecta sozinho: a
/// queda do indicador não pode derrubar a conexão com o servidor.
/// </summary>
/// <remarks>
/// "Online" significa peso válido chegando, não conexão aberta. Uma porta serial abre mesmo sem
/// indicador ligado; se abrir bastasse, o Client transmitiria um peso que ninguém mediu.
/// </remarks>
/// <param name="log">Mensagem e, quando for falha, a exceção.</param>
public sealed class ScaleIndicatorConnection(
    IScaleTransport transport,
    ScaleProtocolOptions options,
    Action<int> onWeight,
    Action<bool> onOnlineChanged,
    Action<string>? onRawFrame,
    Action<string, Exception?> log,
    TimeSpan? retryDelay = null)
{
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReadOnceAsync(ct);
                log($"Indicador {transport.Description} encerrou a transmissão.", null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                log(
                    $"Indicador {transport.Description} sem peso válido há " +
                    $"{transport.SilenceTimeout?.TotalSeconds:0.#} s.",
                    null);
            }
            catch (Exception ex)
            {
                log($"Falha no indicador {transport.Description}.", ex);
            }

            onOnlineChanged(false);

            try
            {
                await Task.Delay(_retryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReadOnceAsync(CancellationToken ct)
    {
        await using var stream = await transport.OpenAsync(ct);

        log($"Indicador {transport.Description} aberto.", null);

        // O prazo de silêncio só é renovado por peso válido: baud rate errado entrega bytes sem
        // parar e nenhum peso, e isso continua sendo um indicador fora do ar.
        using var silence = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var online = false;

        void RestartSilenceTimer()
        {
            if (transport.SilenceTimeout is { } timeout)
                silence.CancelAfter(timeout);
        }

        RestartSilenceTimer();

        await ScaleStreamReader.ReadAsync(
            stream,
            options,
            weight =>
            {
                onWeight(weight);

                if (!online)
                {
                    online = true;
                    onOnlineChanged(true);
                }

                RestartSilenceTimer();
            },
            onRawFrame,
            silence.Token);
    }
}
