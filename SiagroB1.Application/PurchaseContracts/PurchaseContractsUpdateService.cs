using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsUpdateService(AppDbContext context, ILogger<PurchaseContractsUpdateService> logger)
{
    public async Task<PurchaseContract?> ExecuteAsync(Guid key, PurchaseContract entity)
    {
        try
        {
            var existingEntity = await context.Set<PurchaseContract>()
                .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
            context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Save changes
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(key))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                logger.Log(LogLevel.Error, "Failed to update entity.");
                throw new DefaultException("Error updating entity due to concurrency issues.");
            }
        }

        return entity;
    }

    private bool EntityExists(Guid key)
    {
        return context.Set<PurchaseContract>().Any(e => e.Key == key);
    }
}