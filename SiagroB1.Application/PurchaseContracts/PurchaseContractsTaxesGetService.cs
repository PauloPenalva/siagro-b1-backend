using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsTaxesGetService(AppDbContext context, ILogger<PurchaseContractsTaxesGetService> logger)
{
    public async Task<PurchaseContractTax?> GetByIdAsync(Guid key, Guid associationKey)
    {
        try
        {
            if (!ExistPurchaseContract(key))
            {
                throw new NotFoundException("Purchase contract key not found");
            }
            
            return await context.PurchaseContractsTaxes.FindAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<PurchaseContractTax> QueryAll(Guid parentKey)
    {
        return context.PurchaseContractsTaxes
            .Where(x => x.PurchaseContractKey == parentKey)
            .AsNoTracking();
    }

    private bool ExistPurchaseContract(Guid key)
    {
        return context.PurchaseContracts.Any(x => x.Key == key);
    }
}