namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesCalculationOrchestratorService(
    StorageAddressesDailyBalanceBuilderService dailyBalance,
    StorageAddressesStorageChargeCalculatorService storageCalculator,
    StorageAddressesTechnicalLossCalculatorService technicalLossCalculator
    )
{
    public async Task ExecuteAsync(DateTime processingDate, CancellationToken ct = default)
    {
        await dailyBalance.BuildAsync(processingDate, ct);
        await technicalLossCalculator.CalculateAsync(processingDate, ct);
        await dailyBalance.BuildAsync(processingDate, ct);
        await storageCalculator.CalculateAsync(processingDate, ct);
    }
}