using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class TabelaCustoService(AppDbContext context, ILogger<TabelaCustoService> logger)
        : BaseService<TabelaCusto, int>(context, logger), ITabelaCustoService
    {
        public override async Task<TabelaCusto> CreateAsync(TabelaCusto entity)
        {
            try
            {
                await ValidateEntity(entity);

                await _context.TabelasCusto.AddAsync(entity);
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

        public override async Task<TabelaCusto?> GetByIdAsync(int id)
        {
            try
            {
                _logger.LogInformation("Fetching entity with ID {Id}", id);
                return await _context.TabelasCusto
                    .Include(tc => tc.DescontosSecagem)
                    .Include(tc => tc.ValoresSecagem)
                    .Include(tc => tc.Qualidades)
                        .ThenInclude(q => q.CaracteristicaQualidade)
                    .Include(tc => tc.Servicos)
                        .ThenInclude(s => s.ServicoArmazem)
                    .FirstOrDefaultAsync(tc => tc.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", id);
                throw new DefaultException("Error fetching entity");
            }
        }

        public async Task<TabelaCustoDescontoSecagem> CreateDescontosSecagemAsync(int key,TabelaCustoDescontoSecagem t)
        {
            try
            {
                var tb = await GetByIdAsync(key);
                t.TabelaCusto = tb;
                await _context.TabelasCustoDescontoSecagem.AddAsync(t);
                await _context.SaveChangesAsync();
                return t;
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
        
        public IQueryable<TabelaCustoDescontoSecagem> GetDescontosSecagem(int id)
        {
            return _context.TabelasCustoDescontoSecagem
                .Where(x => x.TabelaCustoId == id)
                .AsNoTracking();

        }

        public IQueryable<TabelaCustoValorSecagem> GetValoresSecagem(int id)
        {
            return _context.TabelasCustoValorSecagem
                .Where(x => x.TabelaCustoId == id)
                .AsNoTracking();
        }

        public IQueryable<TabelaCustoServico> GetServicos(int id)
        {
            return _context.TabelasCustoServico
                .Where(x => x.TabelaCustoId == id)
                .AsNoTracking();
        }
        

        public IQueryable<TabelaCustoQualidade> GetQualidades(int id)
        {
            return _context.TabelasCustoQualidade
                .Where(x => x.TabelaCustoId == id)
                .AsNoTracking();
        }

        public override async Task<TabelaCusto?> UpdateAsync(int id, TabelaCusto entity)
        {
            try
            {
                var existingEntity = await _context.TabelasCusto
                    .FirstOrDefaultAsync(tc => tc.Id == id) ?? throw new KeyNotFoundException("Entity not found.");
                _context.Entry(existingEntity).CurrentValues.SetValues(entity);

                // Save changes
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EntityExists("Id", id))
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

        public override async Task<bool> DeleteAsync(int id)
        {
            return await DeleteAsyncWithTransaction(id, async entity =>
            {
                // Carrega filhos explicitamente
                await _context.Entry(entity).Collection(e => e.DescontosSecagem).LoadAsync();
                await _context.Entry(entity).Collection(e => e.ValoresSecagem).LoadAsync();
                await _context.Entry(entity).Collection(e => e.Qualidades).LoadAsync();
                await _context.Entry(entity).Collection(e => e.Servicos).LoadAsync();

                // Remove filhos primeiro (caso o cascade não esteja funcionando)
                if (entity.DescontosSecagem.Any())
                    _context.TabelasCustoDescontoSecagem.RemoveRange(entity.DescontosSecagem);

                if (entity.ValoresSecagem.Any())
                    _context.TabelasCustoValorSecagem.RemoveRange(entity.ValoresSecagem);

                if (entity.Qualidades.Any())
                    _context.TabelasCustoQualidade.RemoveRange(entity.Qualidades);

                if (entity.Servicos.Any())
                    _context.TabelasCustoServico.RemoveRange(entity.Servicos);

                await _context.SaveChangesAsync();
            });
        }

        private async Task ValidateEntity(TabelaCusto entity)
        {
            if (entity.Qualidades != null)
            {
                foreach (var qualidade in entity.Qualidades)
                {
                    if (qualidade.CaracteristicaQualidadeId == 0)
                    {
                        throw new DefaultException("Característica de qualidade inválida na tabela de custo.");
                    }

                    var caracteristica = await _context.CaracteristicasQualidade.FirstOrDefaultAsync(cq => cq.Id == qualidade.CaracteristicaQualidadeId);
                    if (caracteristica == null)
                    {
                        throw new DefaultException($"Característica de qualidade com ID {qualidade.CaracteristicaQualidadeId} não existe.");
                    }
                }
            }

            if (entity.Servicos != null)
            {
                foreach (var servico in entity.Servicos)
                {
                    if (servico.ServicoId   == 0)
                    {
                        throw new DefaultException("Serviço de armazém inválido na tabela de custo.");
                    }

                    var servicoArmazem = await _context.ServicosArmazem.FirstOrDefaultAsync(sa => sa.Id == servico.ServicoId);
                    if (servicoArmazem == null)
                    {
                        throw new DefaultException($"Serviço de armazém com ID {servico.ServicoId} não existe.");
                    }
                }
            }
        }


        public async Task<TabelaCustoServico?> FindTabelaCustoServicoById(int tabelaCustoId, int tabelaCustoServicoId)
        {
            try
            {
                return await _context.TabelasCustoServico
                    .FirstOrDefaultAsync(x => x.TabelaCustoId == tabelaCustoId && x.Id == tabelaCustoServicoId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", tabelaCustoServicoId);
                throw new DefaultException("Error fetching entity");
            }
        }


        public async Task<TabelaCustoServico?> UpdateTabelaCustoServicoAsync(int id, TabelaCustoServico entity)
        {
            try
            {
                var existingEntity = await _context.TabelasCustoServico
                    .FirstOrDefaultAsync(tc => tc.Id == id) ?? throw new KeyNotFoundException("Entity not found.");
                _context.Entry(existingEntity).CurrentValues.SetValues(entity);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EntityExists("Id", id))
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
    }

}