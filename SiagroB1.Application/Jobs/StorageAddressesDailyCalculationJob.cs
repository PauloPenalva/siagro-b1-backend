using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Jobs;

public class StorageAddressesDailyCalculationJob(
    StorageAddressesCalculationOrchestratorService orchestrator,
    IStringLocalizer<Resource> resource,
    ILogger<StorageAddressesDailyCalculationJob> logger)
    : IStorageAddressesDailyCalculationJob
{
    public async Task ExecuteAsync(DateTime? processingDate = null, CancellationToken ct = default)
    {
        var date = (processingDate ?? DateTime.Today.AddDays(-1)).Date;

        logger.LogInformation(resource["MESSAGE_00001"], date);

        try
        {
            await orchestrator.ExecuteAsync(date, ct);

            logger.LogInformation(resource["MESSAGE_00002"], date);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["MESSAGE_00003"], date);

            throw;
        }
    }
}