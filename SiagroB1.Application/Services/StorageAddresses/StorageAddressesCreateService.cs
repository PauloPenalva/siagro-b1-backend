using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageAddresses;

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
            // Sem RollbackAsync aqui: não há BeginTransactionAsync, então a chamada era
            // no-op e só passava a falsa impressão de fluxo transacional. O único
            // SaveChangesAsync acima já é atômico por si. Se um dia este método ganhar
            // um segundo SaveChanges, abrir transação de verdade (Begin/Commit), como em
            // SalesInvoicesConfirmService.
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }  
}