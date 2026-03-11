using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesUpdateService(
    AppDbContext context, 
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    ILogger<StorageAddressesUpdateService> logger)
{
    public async Task<StorageAddress?> ExecuteAsync(string code, StorageAddress entity, string userName)
    {
        var existingAddress = await context.StorageAddresses
                                  .FirstOrDefaultAsync(x => x.Code == code) ??
                              throw new NotFoundException("Storage address not found.");

        if (existingAddress.TransactionOrigin != TransactionCode.StorageAddress)
        {
            throw new ApplicationException("This record was created by another transaction. It cannot be updated.");
        }
        
        try
        {
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.WarehouseName = (await warehouseService.GetByIdAsync(entity.WarehouseCode))?.Name;
            context.Entry(existingAddress).CurrentValues.SetValues(entity);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}