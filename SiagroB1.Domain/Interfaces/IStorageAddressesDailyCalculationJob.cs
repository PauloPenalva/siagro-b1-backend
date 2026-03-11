namespace SiagroB1.Domain.Interfaces;

public interface IStorageAddressesDailyCalculationJob
{
    Task ExecuteAsync(DateTime? processingDate = null, CancellationToken ct = default);
}