using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class PurchaseContractService(AppDbContext context, ILogger<IPurchaseContractService> logger) 
    : BaseService<PurchaseContract, Guid>(context, logger), IPurchaseContractService
{
    public override async Task<PurchaseContract> CreateAsync(PurchaseContract entity)
    {
        try
        {
            foreach (var fixation in entity.PriceFixations)
            {
                fixation.PurchaseContract = entity;
            }
            
            foreach (var tax in entity.Taxes)
            {
                tax.PurchaseContract = entity;
            }

            foreach (var parameter in entity.QualityParameters)
            {
                parameter.PurchaseContract = entity;
            }
            
            await _context.Set<PurchaseContract>().AddAsync(entity);
            await _context.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                throw;
            }

            _logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }

    public override async Task<PurchaseContract?> GetByIdAsync(Guid key)
    {
        try
        {
            _logger.LogInformation("Fetching entity with Code {Code}", key);
            return await _context.Set<PurchaseContract>()
                .Include(pc => pc.PriceFixations)
                .Include(pc => pc.QualityParameters)
                .ThenInclude(qa => qa.QualityAttrib)
                .Include(pc => pc.Taxes)
                .ThenInclude(tx => tx.Tax)
                .FirstOrDefaultAsync(pc => pc.Key == key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching entity with Key {Key}", key);
            throw new DefaultException("Error fetching entity");
        }
    }


    public override async Task<PurchaseContract?> UpdateAsync(Guid key, PurchaseContract entity)
    {
        try
        {
            foreach (var fixation in entity.PriceFixations)
            {
                fixation.PurchaseContract = entity;
            }
            
            foreach (var tax in entity.Taxes)
            {
                tax.PurchaseContract = entity;
            }

            foreach (var parameter in entity.QualityParameters)
            {
                parameter.PurchaseContract = entity;
            }

            
            var existingEntity = await _context.Set<PurchaseContract>()
                .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
            _context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Save changes
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists("Key", key))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                throw new DefaultException("Error updating entity due to concurrency issues.");
            }
        }

        return entity;
    }
}