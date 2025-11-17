using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Domain.Shared.Base.Exceptions;

using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class WeighingTicketService(AppDbContext context, ILogger<WeighingTicketService> logger) 
    : BaseService<WeighingTicket, Guid>(context, logger), IWeighingTicketService
{
    public override async Task<WeighingTicket> CreateAsync(WeighingTicket entity)
    {
        try
        {
            foreach (var detail in entity.QualityInspections)
            {
                detail.WeighingTicket = entity;
            }
            
            await _context.Set<WeighingTicket>().AddAsync(entity);
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
    
    public override async Task<WeighingTicket?> UpdateAsync(Guid code, WeighingTicket entity)
    {
        try
        {
            foreach (var detail in entity.QualityInspections)
            {
                detail.WeighingTicket = entity;
            }
                
            var existingEntity = await _context.Set<WeighingTicket>()
                .FirstOrDefaultAsync(tc => tc.Key == code) ?? throw new KeyNotFoundException("Entity not found.");
            _context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Save changes
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists("Key", code))
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
    
    public override async Task<bool> DeleteAsync(Guid code)
    {
        return await DeleteAsyncWithTransaction(code, async entity =>
        {
            // Carrega filhos explicitamente
            await _context.Entry(entity).Collection(e => e.QualityInspections).LoadAsync();
            
            // Remove filhos primeiro (caso o cascade não esteja funcionando)
            if (entity.QualityInspections.Any())
                _context.Set<QualityInspection>().RemoveRange(entity.QualityInspections);
            
            await _context.SaveChangesAsync();
        });
    }
}