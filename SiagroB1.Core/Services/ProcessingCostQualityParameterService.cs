using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class ProcessingCostQualityParameterService(AppDbContext context, ILogger<ProcessingCostQualityParameterService> logger)
        : BaseService<ProcessingCostQualityParameter, int>(context, logger), IProcessingCostQualityParameterService
    {
        public async Task<ProcessingCostQualityParameter> CreateAsync(string processingCostKey, ProcessingCostQualityParameter entity)
        {
            var tb = await _context.Set<ProcessingCost>().FirstOrDefaultAsync(x => x.Key == processingCostKey)
                ?? throw new KeyNotFoundException("");

            try
            {
                entity.ProcessingCost = tb;

                await _context.Set<ProcessingCostQualityParameter>().AddAsync(entity);
                await _context.SaveChangesAsync();

                return entity;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating entity.");
                throw new DefaultException("Error creating entity.");
            }
        }

        public async Task<ProcessingCostQualityParameter?> FindByKeyAsync(string processingCostKey, int itemId)
        {
            try
            {
                return await _context.Set<ProcessingCostQualityParameter>()
                    .Where(x => x.ProcessingCostKey == processingCostKey && x.ItemId == itemId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", itemId);
                throw new DefaultException("Error fetching entity");
            }
        }
        
        public IQueryable<ProcessingCostQualityParameter> GetAllByProcessingCostKey(string processingCostKey)
        {
            return _context.Set<ProcessingCostQualityParameter>()
                .Where(x => x.ProcessingCostKey == processingCostKey);
                
        }
    }

}