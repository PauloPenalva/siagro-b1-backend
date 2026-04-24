namespace SiagroB1.Client.Interfaces;

public interface IScaleReader
{
    decimal GetWeight();
    Task StartAsync(CancellationToken ct);
}