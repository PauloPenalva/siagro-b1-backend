using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsDeleteService(
    AppDbContext context, ILogger<SalesContractsDeliveryLocationsDeleteService> logger)
{
    public Task<bool> ExecuteAsync(Guid associationKey) => Delete(associationKey);

    public async Task<bool> ExecuteAsync(Guid parentKey, Guid associationKey)
    {
        if (!context.SalesContracts.Any(x => x.Key == parentKey))
            throw new NotFoundException("Sales contract not found");
        return await Delete(associationKey);
    }

    private async Task<bool> Delete(Guid associationKey)
    {
        try
        {
            var existingEntity = await context.SalesContractsDeliveryLocations.FindAsync(associationKey)
                ?? throw new NotFoundException("Delivery location not found");

            context.SalesContractsDeliveryLocations.Remove(existingEntity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }
}
