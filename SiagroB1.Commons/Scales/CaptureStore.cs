using System.Collections.Concurrent;

namespace SiagroB1.Commons.Scales;

/// <summary>Comprovante de que um peso saiu da balança, e não do teclado.</summary>
public sealed record WeightCapture(
    Guid CaptureId,
    string ScaleCode,
    int Weight,
    string Username,
    DateTime ExpiresAt);

/// <summary>
/// Guarda os comprovantes emitidos. Uso único e com validade: um comprovante consumido não volta,
/// e um antigo não serve para gravar uma pesagem de hoje.
/// </summary>
public sealed class CaptureStore(TimeSpan ttl)
{
    private readonly ConcurrentDictionary<Guid, WeightCapture> _captures = new();

    public WeightCapture Create(string scaleCode, int weight, string username, DateTime now)
    {
        Purge(now);

        var capture = new WeightCapture(Guid.NewGuid(), scaleCode, weight, username, now + ttl);

        _captures[capture.CaptureId] = capture;

        return capture;
    }

    /// <summary>
    /// Devolve o comprovante e o remove. Nulo quando não existe, expirou ou é de outro usuário -
    /// quem chama trata os três casos com a mesma mensagem, para não virar oráculo de captura alheia.
    /// </summary>
    public WeightCapture? Consume(Guid captureId, string username, DateTime now)
    {
        if (!_captures.TryRemove(captureId, out var capture))
            return null;

        if (capture.ExpiresAt < now)
            return null;

        return string.Equals(capture.Username, username, StringComparison.OrdinalIgnoreCase)
            ? capture
            : null;
    }

    private void Purge(DateTime now)
    {
        foreach (var expired in _captures.Where(x => x.Value.ExpiresAt < now).Select(x => x.Key))
            _captures.TryRemove(expired, out _);
    }
}
