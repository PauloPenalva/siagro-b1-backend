namespace SiagroB1.Commons.Market;

/// <summary>
/// Limita um log repetitivo (falha que se repete a cada tick) a uma linha por intervalo; a primeira
/// ocorrência sempre passa. Não é thread-safe: um por laço/sessão.
/// </summary>
public sealed class LogThrottle(TimeSpan interval)
{
    private DateTimeOffset? _last;

    public bool ShouldLog(DateTimeOffset now)
    {
        if (_last is { } last && now - last < interval)
            return false;

        _last = now;
        return true;
    }
}
