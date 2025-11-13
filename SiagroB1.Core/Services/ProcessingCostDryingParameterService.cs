using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class ProcessingCostDryingParameterService(AppDbContext context, ILogger<ProcessingCostDryingParameterService> logger)
        : BaseService<ProcessingCostDryingParameter, Guid>(context, logger), IProcessingCostDryingParameterService
    {
        public async Task<ProcessingCostDryingParameter> CreateAsync(Guid processingCostKey, ProcessingCostDryingParameter entity)
        {
            var tb = await _context.Set<ProcessingCost>().FirstOrDefaultAsync(x => x.Key == processingCostKey)
                ?? throw new KeyNotFoundException("");

            try
            {
                entity.ProcessingCost = tb;

                await _context.Set<ProcessingCostDryingParameter>().AddAsync(entity);
                await _context.SaveChangesAsync();

                return entity;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating entity.");
                throw new DefaultException("Error creating entity.");
            }
        }

        public async Task<ProcessingCostDryingParameter?> FindByKeyAsync(Guid processingCostKey, Guid key)
        {
            try
            {
                return await _context.Set<ProcessingCostDryingParameter>()
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
        
        public IQueryable<ProcessingCostDryingParameter> GetAllByTabelaCustoId(Guid processingCostKey)
        {
            return _context.Set<ProcessingCostDryingParameter>()
                .Where(x => x.ProcessingCostKey == processingCostKey);
                
        }
    }

}