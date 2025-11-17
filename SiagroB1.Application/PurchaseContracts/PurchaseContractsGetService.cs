using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsGetService(AppDbContext context, ILogger<PurchaseContractsUpdateService> logger)
{
    public async Task<PurchaseContract?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await context.PurchaseContracts
                .Include(p => p.PriceFixations)
                .Include(p => p.QualityParameters)
                .ThenInclude(qa => qa.QualityAttrib)
                .Include(p => p.Taxes)
                .ThenInclude(t => t.Tax)
                .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<PurchaseContract> QueryAll()
    {
        return context.PurchaseContracts.AsNoTracking();
    }
}