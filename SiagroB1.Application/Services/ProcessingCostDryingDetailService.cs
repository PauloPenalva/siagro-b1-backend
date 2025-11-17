using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class ProcessingCostDryingDetailService(AppDbContext context, ILogger<ProcessingCostDryingDetailService> logger)
    : BaseService<ProcessingCostDryingDetail, string>(context, logger), IProcessingCostDryingDetailService
{
    
    public async Task<ProcessingCostDryingDetail> CreateAsync(string processingCostCode, ProcessingCostDryingDetail entity)
    {
        var tb = await _context.Set<ProcessingCost>().FirstOrDefaultAsync(x => x.Code == processingCostCode)
            ?? throw new KeyNotFoundException("");

        try
        {
            entity.ProcessingCost = tb;

            await _context.Set<ProcessingCostDryingDetail>().AddAsync(entity);
            await _context.SaveChangesAsync();

            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }

    public async Task<ProcessingCostDryingDetail?> FindByKeyAsync(string processingCostCode, int itemId)
    {
        try
        {
            return await _context.Set<ProcessingCostDryingDetail>()
                .Where(x => x.ProcessingCostCode == processingCostCode && x.ItemId == itemId)
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching entity with ID {Id}", itemId);
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public IQueryable<ProcessingCostDryingDetail> GetAllByProcessingCostKey(string processingCostCode)
    {
        return _context.Set<ProcessingCostDryingDetail>()
            .Where(x => x.ProcessingCostCode == processingCostCode);
            
    }
}

