using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class ProcessingCostServiceDetailService(AppDbContext context, ILogger<ProcessingCostServiceDetailService> logger)
        : BaseService<ProcessingCostServiceDetail, string>(context, logger), IProcessingCostServiceDetailService
    {
        public async Task<ProcessingCostServiceDetail> CreateAsync(string processingCostKey, ProcessingCostServiceDetail entity)
        {
            var tb = await _context.Set<ProcessingCost>().
                         FirstOrDefaultAsync(x => x.Key == processingCostKey) 
                     ?? throw new KeyNotFoundException("");

            try
            {
                entity.ProcessingCost = tb;

                await _context.Set<ProcessingCostServiceDetail>().AddAsync(entity);
                await _context.SaveChangesAsync();

                return entity;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating entity.");
                throw new DefaultException("Error creating entity.");
            }
        }

        public async Task<ProcessingCostServiceDetail?> FindByKeyAsync(string processingCostKey, string key)
        {
            try
            {
                return await _context.Set<ProcessingCostServiceDetail>()
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
        
        public IQueryable<ProcessingCostServiceDetail> GetAllByProcessingCostKey(string processingCostKey)
        {
            return _context.Set<ProcessingCostServiceDetail>()
                .Where(x => x.ProcessingCostKey == processingCostKey);
                
        }
    }

}