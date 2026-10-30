namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesCalculationOrchestratorService(
    StorageAddressesDailyBalanceBuilderService dailyBalance,
    StorageAddressesStorageChargeCalculatorService storageCalculator,
    StorageAddressesTechnicalLossCalculatorService technicalLossCalculator
    )
{
    /// <param name="storageAddressCode">
    /// Só este lote (reprocessamento, GAC-18); nulo = todos (job diário). O reprocessamento apaga
    /// os dados de UM lote, então recalcular os outros duplicava as quebras deles.
    /// </param>
    public async Task ExecuteAsync(DateTime processingDate, CancellationToken ct = default, string? storageAddressCode = null)
    {
        await dailyBalance.BuildAsync(processingDate, ct, storageAddressCode);
        await technicalLossCalculator.CalculateAsync(processingDate, ct, storageAddressCode);
        await dailyBalance.BuildAsync(processingDate, ct, storageAddressCode);
        await storageCalculator.CalculateAsync(processingDate, ct, storageAddressCode);
    }
}