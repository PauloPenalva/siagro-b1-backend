using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesUpdateService(
    IUnitOfWork db, 
    BusinessPartnerService businessPartnerService,
    ILogger<SalesInvoicesUpdateService> logger)
{
    public async Task<SalesInvoice?> ExecuteAsync(Guid key, SalesInvoice entity, string userName)
    {
        var existingEntity = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
        
        try
        {
            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);

            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}