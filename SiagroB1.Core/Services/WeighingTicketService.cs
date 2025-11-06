using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services;

public class WeighingTicketService(AppDbContext context, ILogger<WeighingTicketService> logger) 
    : BaseService<WeighingTicket, string>(context, logger), IWeighingTicketService
{
    public override async Task<WeighingTicket> CreateAsync(WeighingTicket entity)
    {
        try
        {
            if (string.IsNullOrEmpty(entity.Key))
            {
                throw new ArgumentException("Key cannot be null or empty.");
            }
            
            foreach (var detail in entity.QualityInspections)
            {
                detail.BranchKey = entity.BranchKey;
                detail.WeighingTicketKey = entity.Key;
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
    
    public override async Task<WeighingTicket?> UpdateAsync(string key, WeighingTicket entity)
    {
        try
        {
            if (string.IsNullOrEmpty(entity.Key))
            {
                throw new ArgumentException("Key cannot be null or empty.");
            }
            
            foreach (var detail in entity.QualityInspections)
            {
                detail.BranchKey = entity.BranchKey;
                detail.WeighingTicketKey = entity.Key;
            }
                
            var existingEntity = await _context.Set<WeighingTicket>()
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
    
    public override async Task<bool> DeleteAsync(string key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
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