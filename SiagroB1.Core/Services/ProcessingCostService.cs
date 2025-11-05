using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class ProcessingCostService(AppDbContext context, ILogger<ProcessingCostService> logger)
        : BaseService<ProcessingCost, string>(context, logger), IProcessingCostService
    {
        public override async Task<ProcessingCost> CreateAsync(ProcessingCost entity)
        {
            try
            {
                await ValidateEntity(entity);
                
                foreach (var detail in entity.DryingDetails)
                {
                    detail.ProcessingCostKey = entity.Key;
                }
                
                foreach (var parameter in entity.DryingParameters)
                {
                    parameter.ProcessingCostKey = entity.Key;
                }

                foreach (var parameter in entity.QualityParameters)
                {
                    parameter.ProcessingCostKey = entity.Key;
                }

                foreach (var detail in entity.ServiceDetails)
                {
                    detail.ProcessingCostKey = entity.Key;
                }

                await _context.Set<ProcessingCost>().AddAsync(entity);
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

        public override async Task<ProcessingCost?> GetByIdAsync(string key)
        {
            try
            {
                _logger.LogInformation("Fetching entity with Key {Key}", key);
                return await _context.Set<ProcessingCost>()
                    .Include(tc => tc.DryingParameters)
                    .Include(tc => tc.DryingDetails)
                    .Include(tc => tc.QualityParameters)
                        .ThenInclude(q => q.QualityAttrib)
                    .Include(tc => tc.ServiceDetails)
                        .ThenInclude(s => s.ProcessingService)
                    .FirstOrDefaultAsync(tc => tc.Key == key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", key);
                throw new DefaultException("Error fetching entity");
            }
        }

        public async Task<ProcessingCostDryingParameter> CreateDescontosSecagemAsync(string key,ProcessingCostDryingParameter t)
        {
            try
            {
                var tb = await GetByIdAsync(key);
                t.ProcessingCost = tb;
                await _context.Set<ProcessingCostDryingParameter>().AddAsync(t);
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
        
        public IQueryable<ProcessingCostDryingParameter> GetDescontosSecagem(string key)
        {
            return _context.Set<ProcessingCostDryingParameter>()
                .Where(x => x.ProcessingCostKey == key)
                .AsNoTracking();

        }

        public IQueryable<ProcessingCostDryingDetail> GetValoresSecagem(string key)
        {
            return _context.Set<ProcessingCostDryingDetail>()
                .Where(x => x.ProcessingCostKey == key)
                .AsNoTracking();
        }

        public IQueryable<ProcessingCostServiceDetail> GetServicos(string key)
        {
            return _context.Set<ProcessingCostServiceDetail>()
                .Where(x => x.ProcessingCostKey == key)
                .AsNoTracking();
        }
        

        public IQueryable<ProcessingCostQualityParameter> GetQualidades(string key)
        {
            return _context.Set<ProcessingCostQualityParameter>()
                .Where(x => x.ProcessingCostKey == key)
                .AsNoTracking();
        }

        public override async Task<ProcessingCost?> UpdateAsync(string key, ProcessingCost entity)
        {
            try
            {
                foreach (var detail in entity.DryingDetails)
                {
                    detail.ProcessingCostKey = entity.Key;
                }
                
                foreach (var parameter in entity.DryingParameters)
                {
                    parameter.ProcessingCostKey = entity.Key;
                }

                foreach (var parameter in entity.QualityParameters)
                {
                    parameter.ProcessingCostKey = entity.Key;
                }

                foreach (var detail in entity.ServiceDetails)
                {
                    detail.ProcessingCostKey = entity.Key;
                }
                
                var existingEntity = await _context.Set<ProcessingCost>()
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
                await _context.Entry(entity).Collection(e => e.DryingParameters).LoadAsync();
                await _context.Entry(entity).Collection(e => e.DryingDetails).LoadAsync();
                await _context.Entry(entity).Collection(e => e.QualityParameters).LoadAsync();
                await _context.Entry(entity).Collection(e => e.ServiceDetails).LoadAsync();

                // Remove filhos primeiro (caso o cascade não esteja funcionando)
                if (entity.DryingParameters.Any())
                    _context.Set<ProcessingCostDryingParameter>().RemoveRange(entity.DryingParameters);

                if (entity.DryingDetails.Any())
                    _context.Set<ProcessingCostDryingDetail>().RemoveRange(entity.DryingDetails);

                if (entity.QualityParameters.Any())
                    _context.Set<ProcessingCostQualityParameter>().RemoveRange(entity.QualityParameters);

                if (entity.ServiceDetails.Any())
                    _context.Set<ProcessingCostServiceDetail>().RemoveRange(entity.ServiceDetails);

                await _context.SaveChangesAsync();
            });
        }

        private async Task ValidateEntity(ProcessingCost entity)
        {
            if (entity.QualityParameters != null)
            {
                foreach (var parameter in entity.QualityParameters)
                {
                    if (string.IsNullOrEmpty(parameter.QualityAttribKey))
                    {
                        throw new DefaultException("Característica de qualidade inválida na tabela de custo.");
                    }

                    var attrib = await _context.Set<QualityAttrib>()
                        .FirstOrDefaultAsync(cq => cq.Key == parameter.QualityAttribKey);
                    if (attrib == null)
                    {
                        throw new DefaultException($"Característica de qualidade com ID {parameter.QualityAttribKey} não existe.");
                    }
                }
            }

            if (entity.ServiceDetails != null)
            {
                foreach (var detail in entity.ServiceDetails)
                {
                    if (string.IsNullOrEmpty(detail.ProcessingServiceKey))
                    {
                        throw new DefaultException("Serviço de armazém inválido na tabela de custo.");
                    }

                    var servicoArmazem = await _context.Set<Domain.Entities.ProcessingService>()
                        .FirstOrDefaultAsync(sa => sa.Key == detail.ProcessingServiceKey);
                    if (servicoArmazem == null)
                    {
                        throw new DefaultException($"Serviço de armazém com ID {detail.ProcessingServiceKey} não existe.");
                    }
                }
            }
        }


        public async Task<ProcessingCostServiceDetail?> FindTabelaCustoServicoById(string processingCostKey, string processingServiceKey)
        {
            try
            {
                return await _context.Set<ProcessingCostServiceDetail>()
                    .FirstOrDefaultAsync(x => x.ProcessingCostKey == processingCostKey && x.ProcessingServiceKey == processingServiceKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching entity with ID {Id}", processingServiceKey);
                throw new DefaultException("Error fetching entity");
            }
        }


        public async Task<ProcessingCostServiceDetail?> UpdateTabelaCustoServicoAsync(string key, ProcessingCostServiceDetail entity)
        {
            try
            {
                var existingEntity = await _context.Set<ProcessingCostServiceDetail>()
                    .FirstOrDefaultAsync(tc => tc.ProcessingServiceKey == key) ?? throw new KeyNotFoundException("Entity not found.");
                _context.Entry(existingEntity).CurrentValues.SetValues(entity);
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
    }

}