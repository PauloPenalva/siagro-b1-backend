namespace SiagroB1.Client.Mock;

/// <summary>
/// Balança simulada. Sobe até um alvo, ESTABILIZA por alguns segundos e depois muda de alvo - o
/// mock anterior subia para sempre e nunca estabilizava, então não exercitava a captura.
/// </summary>
public sealed class MockScaleReader(Action<int> onWeight, Action<bool> onConnectionChanged)
{
    private readonly Random _random = new();

    public async Task RunAsync(CancellationToken ct)
    {
        onConnectionChanged(true);

        var current = 0;
        var target = _random.Next(15000, 45000);
        var stableSince = DateTime.Now;

        while (!ct.IsCancellationRequested)
        {
            if (current < target)
            {
                current = Math.Min(current + 1500, target);
                stableSince = DateTime.Now;
            }
            else if (DateTime.Now - stableSince > TimeSpan.FromSeconds(20))
            {
                target = _random.Next(15000, 45000);
                current = 0;
            }

            onWeight(current);

            try
            {
                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        onConnectionChanged(false);
    }
}
