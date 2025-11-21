using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsCreateService(AppDbContext context, ILogger<SalesContractsCreateService> logger)
{
    public async Task<SalesContract> ExecuteAsync(SalesContract entity, string createdBy)
    {
        try
        {
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
            entity.Status = ContractStatus.Draft;
            await context.SalesContracts.AddAsync(entity);
            await context.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException("Unable to create sales contract.");
        }
    }    
}