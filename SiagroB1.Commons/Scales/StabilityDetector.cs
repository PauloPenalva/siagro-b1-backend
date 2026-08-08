namespace SiagroB1.Commons.Scales;

/// <summary>
/// Peso estável = todas as leituras da janela iguais entre si, com um mínimo de amostras. Uma
/// leitura diferente reinicia a janela: é o que impede gravar o peso de um caminhão em movimento.
/// </summary>
public sealed class StabilityDetector(TimeSpan window, int minimumSamples)
{
    private readonly List<ScaleReading> _readings = [];

    public int Current { get; private set; }

    public void Add(int weight, DateTime now)
    {
        if (_readings.Count > 0 && Current != weight)
            _readings.Clear();

        Current = weight;
        _readings.Add(new ScaleReading(weight, now));

        Trim(now);
    }

    public bool IsStable(DateTime now)
    {
        Trim(now);

        return _readings.Count >= minimumSamples;
    }

    private void Trim(DateTime now)
    {
        var cutoff = now - window;

        _readings.RemoveAll(x => x.Timestamp < cutoff);
    }
}
