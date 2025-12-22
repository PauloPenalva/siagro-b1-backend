using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesCreateService(
    IUnitOfWork db, 
    DocNumbersSequenceService docNumberSequenceService,
    BusinessPartnerService  businessPartnerService,
    ItemService itemService,
    ILogger<StorageAddressesCreateService> logger)
{
    public async Task<StorageAddress> ExecuteAsync(StorageAddress entity, string userName)
    {
        if (entity.DocNumberKey is null)
        {
            var docNumbers = await docNumberSequenceService.GetDocNumbersSeries(TransactionCode.StorageAddress);
            var docNumber = docNumbers.FirstOrDefault(x => x.Default);
            if (docNumber == null)
                throw new ApplicationException("Document Number is empty or not setting default value.");

            entity.DocNumberKey = docNumber.Key;
        }
        
        try
        {
            await db.BeginTransactionAsync();
            
            var docNumber = await docNumberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            
            var currentNumber = docNumber.NextNumber;

            entity.Code = DocNumbersSequenceService
                .FormatNumber(currentNumber, int.Parse(docNumber.NumberSize), docNumber.Prefix, docNumber.Suffix);
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.TransactionOrigin = TransactionCode.StorageAddress;
            
            await db.Context.StorageAddresses.AddAsync(entity);
            await db.SaveChangesAsync();
            
            await docNumberSequenceService.UpdateLastNumber((Guid) entity.DocNumberKey, currentNumber);

            await db.CommitAsync();
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