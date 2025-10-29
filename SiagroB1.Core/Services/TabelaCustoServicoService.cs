using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class TabelaCustoServicoService(AppDbContext context, ILogger<TabelaCustoServicoService> logger)
        : BaseService<TabelaCustoServico, int>(context, logger), ITabelaCustoServicoService
    {
        public async Task<TabelaCustoServico> CreateAsync(int tabelaCustoId, TabelaCustoServico entity)
        {
            var tb = await _context.TabelasCusto.FirstOrDefaultAsync(x => x.Id == tabelaCustoId)
                ?? throw new KeyNotFoundException("");

            try
            {
                entity.TabelaCusto = tb;

                await _context.TabelasCustoServico.AddAsync(entity);
                await _context.SaveChangesAsync();

                return entity;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating entity.");
                throw new DefaultException("Error creating entity.");
            }
        }

        public async Task<TabelaCustoServico?> FindByKeyAsync(int tabelaCustoId, int key)
        {
            try
            {
                return await _context.TabelasCustoServico
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
        
        public IQueryable<TabelaCustoServico> GetAllByTabelaCustoId(int tabelaCustoId)
        {
            return _context.TabelasCustoServico
                .Where(x => x.TabelaCustoId == tabelaCustoId);
                
        }
    }

}