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
        : BaseService<ProcessingCostQualityParameter, Guid>(context, logger), IProcessingCostQualityParameterService
    {
        public async Task<ProcessingCostQualityParameter> CreateAsync(Guid processingCostKey, ProcessingCostQualityParameter entity)
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

        public async Task<ProcessingCostQualityParameter?> FindByKeyAsync(Guid processingCostKey, Guid key)
        {
            try
            {
                return await _context.Set<ProcessingCostQualityParameter>()
                    .Where(x => x.ProcessingCostKey == processingCostKey && x.Key == key)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", key);
                throw new DefaultException("Error fetching entity");
            }
        }
        
        public IQueryable<ProcessingCostQualityParameter> GetAllByProcessingCostKey(Guid processingCostKey)
        {
            return _context.Set<ProcessingCostQualityParameter>()
                .Where(x => x.ProcessingCostKey == processingCostKey);
                
        }
    }

}