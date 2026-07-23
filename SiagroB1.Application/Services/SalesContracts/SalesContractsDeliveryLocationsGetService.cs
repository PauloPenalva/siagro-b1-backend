using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsGetService(
    AppDbContext context, ILogger<SalesContractsDeliveryLocationsGetService> logger)
{
    public async Task<SalesContractDeliveryLocation?> GetByIdAsync(Guid associationKey)
    {
        try
        {
            return await context.SalesContractsDeliveryLocations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", associationKey);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<SalesContractDeliveryLocation?> GetByIdAsync(Guid key, Guid associationKey)
    {
        try
        {
            if (!context.SalesContracts.Any(x => x.Key == key))
                throw new NotFoundException("Sales contract key not found");
            return await context.SalesContractsDeliveryLocations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<SalesContractDeliveryLocation> QueryAll(Guid parentKey) =>
        context.SalesContractsDeliveryLocations
            .Where(x => x.SalesContractKey == parentKey)
            .AsNoTracking();
}
