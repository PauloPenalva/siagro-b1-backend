using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesCreateService(
    IUnitOfWork db, 
    DocNumberSequenceService numberSequenceService,
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    ILogger<StorageAddressesCreateService> logger)
{
    public async Task<StorageAddress> ExecuteAsync(StorageAddress entity, string userName)
    {
        entity.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.StorageAddress);
        
        try
        {
            entity.Code = await numberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.WarehouseName = (await warehouseService.GetByIdAsync(entity.WarehouseCode))?.Name;
            entity.TransactionOrigin = TransactionCode.StorageAddress;
            
            await db.Context.StorageAddresses.AddAsync(entity);
            await db.SaveChangesAsync();
            
            return entity;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }  
}