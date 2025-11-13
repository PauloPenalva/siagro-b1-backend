using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class ProcessingCostDryingDetailService(AppDbContext context, ILogger<ProcessingCostDryingDetailService> logger)
        : BaseService<ProcessingCostDryingDetail, Guid>(context, logger), IProcessingCostDryingDetailService
    {
        
        public async Task<ProcessingCostDryingDetail> CreateAsync(Guid processingCostKey, ProcessingCostDryingDetail entity)
        {
            var tb = await _context.Set<ProcessingCost>().FirstOrDefaultAsync(x => x.Key == processingCostKey)
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

        public async Task<ProcessingCostDryingDetail?> FindByKeyAsync(Guid processingCostKey, Guid itemId)
        {
            try
            {
                return await _context.Set<ProcessingCostDryingDetail>()
                    .Where(x => x.ProcessingCostKey == processingCostKey && x.Key == itemId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", itemId);
                throw new DefaultException("Error fetching entity");
            }
        }
        
        public IQueryable<ProcessingCostDryingDetail> GetAllByProcessingCostKey(Guid processingCostKey)
        {
            return _context.Set<ProcessingCostDryingDetail>()
                .Where(x => x.ProcessingCostKey == processingCostKey);
                
        }
    }

}