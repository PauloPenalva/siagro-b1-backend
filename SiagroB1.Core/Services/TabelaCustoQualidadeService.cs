using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class TabelaCustoQualidadeService(AppDbContext context, ILogger<TabelaCustoQualidadeService> logger)
        : BaseService<TabelaCustoQualidade, int>(context, logger), ITabelaCustoQualidadeService
    {
        public async Task<TabelaCustoQualidade> CreateAsync(int tabelaCustoId, TabelaCustoQualidade entity)
        {
            var tb = await _context.TabelasCusto.FirstOrDefaultAsync(x => x.Id == tabelaCustoId)
                ?? throw new KeyNotFoundException("");

            try
            {
                entity.TabelaCusto = tb;

                await _context.TabelasCustoQualidade.AddAsync(entity);
                await _context.SaveChangesAsync();

                return entity;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating entity.");
                throw new DefaultException("Error creating entity.");
            }
        }

        public async Task<TabelaCustoQualidade?> FindByKeyAsync(int tabelaCustoId, int key)
        {
            try
            {
                return await _context.TabelasCustoQualidade
                    .Where(x => x.TabelaCustoId == tabelaCustoId && x.Id == key)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", key);
                throw new DefaultException("Error fetching entity");
            }
        }
        
        public IQueryable<TabelaCustoQualidade> GetAllByTabelaCustoId(int tabelaCustoId)
        {
            return _context.TabelasCustoQualidade
                .Where(x => x.TabelaCustoId == tabelaCustoId);
                
        }
    }

}