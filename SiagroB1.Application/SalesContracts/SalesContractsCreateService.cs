using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsCreateService(
    IUnitOfWork db, 
    BusinessPartnerService  businessPartnerService,
    ItemService itemService,
    DocNumberSequenceService numberSequenceService,
    ILogger<SalesContractsCreateService> logger)
{
    private static readonly TransactionCode TransactionCode = TransactionCode.SalesContract;

    public async Task<SalesContract> ExecuteAsync(SalesContract entity, string createdBy)
    {
       
        try
        {
            await db.BeginTransactionAsync();
            
            entity.Code = await numberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.Status = ContractStatus.Draft;
            
            await db.Context.SalesContracts.AddAsync(entity);
            await db.SaveChangesAsync();
            
            await db.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
            logger.LogError(ex, ex.Message);
            throw new ApplicationException("Unable to create sales contract.");
        }
    }
}