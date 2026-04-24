using SiagroB1.Client.Interfaces;

namespace SiagroB1.Client.Mock;

public class MockScaleReader : IScaleReader
{
    private decimal _current = 0;
    private readonly Random _rnd = new();

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public decimal GetWeight()
    {
        if (_current < 20000)
            _current += _rnd.Next(500, 1500);

        return _current;
    }
}