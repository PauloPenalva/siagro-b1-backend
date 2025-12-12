using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAllocationGetService(AppDbContext context, ILogger<PurchaseContractsAllocationGetService> logger)
{
    public async Task<PurchaseContractAllocation?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await context.PurchaseContractsAllocations
                .Include(p => p.PurchaseContract)
                .Include(p => p.StorageTransaction)
                .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<PurchaseContractAllocation> QueryAll()
    {
        return context.PurchaseContractsAllocations.AsNoTracking();
    }
}