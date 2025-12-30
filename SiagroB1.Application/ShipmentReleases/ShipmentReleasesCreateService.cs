using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesCreateService(
    IUnitOfWork db,
    ILogger<ShipmentReleasesCreateService> logger)
{
    public async Task<ShipmentRelease> ExecuteAsync(ShipmentRelease entity, string userName)
    {
        var purchaseContract = await db.Context.PurchaseContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == entity.PurchaseContractKey) ??
                               throw new NotFoundException("Purchase contract not found.");
        
        try
        {
            entity.BranchCode = purchaseContract.BranchCode;
            entity.Status = ReleaseStatus.Pending;
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = userName;
            await db.Context.ShipmentReleases.AddAsync(entity);
            await db.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                throw;
            }

            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }    
}