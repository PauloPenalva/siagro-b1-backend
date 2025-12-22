using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesCreateService(
    AppDbContext context, 
    DocTypesService  docTypesService,
    BusinessPartnerService  businessPartnerService,
    ItemService itemService,
    ILogger<StorageAddressesCreateService> logger)
{
    public async Task<StorageAddress> ExecuteAsync(StorageAddress entity, string userName)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var docType = await docTypesService.GetDocType(TransactionCode.StorageAddress);

            var currentNumber = docType.NextNumber;

            entity.Code = FormatStorageAddressCode(currentNumber);
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.TransactionOrigin = TransactionCode.StorageAddress;
            
            await context.StorageAddresses.AddAsync(entity);
            await context.SaveChangesAsync();
            
            await docTypesService.UpdateLastNumber(docType.Code, currentNumber, TransactionCode.StorageAddress);
            
            await transaction.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }  
    
    private string FormatStorageAddressCode(int currentNumber)
    {
        return currentNumber
            .ToString()
            .PadLeft(10, '0');
    }
}