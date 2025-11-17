using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

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
                detail.ProcessingCostCode = entity.Code;
            }
            
            foreach (var parameter in entity.DryingParameters)
            {
                parameter.ProcessingCostCode = entity.Code;
            }

            foreach (var parameter in entity.QualityParameters)
            {
                parameter.ProcessingCostCode = entity.Code;
            }

            foreach (var detail in entity.ServiceDetails)
            {
                detail.ProcessingCostCode = entity.Code;
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

    public override async Task<ProcessingCost?> GetByIdAsync(string code)
    {
        try
        {
            _logger.LogInformation("Fetching entity with Code {Code}", code);
            return await _context.Set<ProcessingCost>()
                .Include(tc => tc.DryingParameters)
                .Include(tc => tc.DryingDetails)
                .Include(tc => tc.QualityParameters)
                    .ThenInclude(q => q.QualityAttrib)
                .Include(tc => tc.ServiceDetails)
                    .ThenInclude(s => s.ProcessingService)
                .FirstOrDefaultAsync(tc => tc.Code == code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<ProcessingCostDryingParameter> CreateDescontosSecagemAsync(string code,ProcessingCostDryingParameter t)
    {
        try
        {
            var tb = await GetByIdAsync(code);
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
    
    public IQueryable<ProcessingCostDryingParameter> GetDescontosSecagem(string code)
    {
        return _context.Set<ProcessingCostDryingParameter>()
            .Where(x => x.ProcessingCostCode == code)
            .AsNoTracking();

    }

    public IQueryable<ProcessingCostDryingDetail> GetValoresSecagem(string code)
    {
        return _context.Set<ProcessingCostDryingDetail>()
            .Where(x => x.ProcessingCostCode == code)
            .AsNoTracking();
    }

    public IQueryable<ProcessingCostServiceDetail> GetServicos(string code)
    {
        return _context.Set<ProcessingCostServiceDetail>()
            .Where(x => x.ProcessingCostCode == code)
            .AsNoTracking();
    }
    

    public IQueryable<ProcessingCostQualityParameter> GetQualidades(string code)
    {
        return _context.Set<ProcessingCostQualityParameter>()
            .Where(x => x.ProcessingCostCode == code)
            .AsNoTracking();
    }

    public override async Task<ProcessingCost?> UpdateAsync(string code, ProcessingCost entity)
    {
        try
        {
            foreach (var detail in entity.DryingDetails)
            {
                detail.ProcessingCostCode = entity.Code;
            }
            
            foreach (var parameter in entity.DryingParameters)
            {
                parameter.ProcessingCostCode = entity.Code;
            }

            foreach (var parameter in entity.QualityParameters)
            {
                parameter.ProcessingCostCode = entity.Code;
            }

            foreach (var detail in entity.ServiceDetails)
            {
                detail.ProcessingCostCode = entity.Code;
            }
            
            var existingEntity = await _context.Set<ProcessingCost>()
                .FirstOrDefaultAsync(tc => tc.Code == code) ?? throw new KeyNotFoundException("Entity not found.");
            _context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Save changes
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists("Code", code))
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

    public override async Task<bool> DeleteAsync(string code)
    {
        return await DeleteAsyncWithTransaction(code, async entity =>
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
                if (parameter.QualityAttribCode.Equals(null))
                {
                    throw new DefaultException("Característica de qualidade inválida na tabela de custo.");
                }

                var attrib = await _context.Set<QualityAttrib>()
                    .FirstOrDefaultAsync(cq => cq.Code == parameter.QualityAttribCode);
                if (attrib == null)
                {
                    throw new DefaultException($"Característica de qualidade com ID {parameter.QualityAttribCode} não existe.");
                }
            }
        }

        if (entity.ServiceDetails != null)
        {
            foreach (var detail in entity.ServiceDetails)
            {
                if (detail.ProcessingServiceCode.Equals(null))
                {
                    throw new DefaultException("Serviço de armazém inválido na tabela de custo.");
                }

                var servicoArmazem = await _context.Set<ProcessingService>()
                    .FirstOrDefaultAsync(sa => sa.Code == detail.ProcessingServiceCode);
                if (servicoArmazem == null)
                {
                    throw new DefaultException($"Serviço de armazém com ID {detail.ProcessingServiceCode} não existe.");
                }
            }
        }
    }


    public async Task<ProcessingCostServiceDetail?> FindTabelaCustoServicoById(string processingCostCode, string processingServiceCode)
    {
        try
        {
            return await _context.Set<ProcessingCostServiceDetail>()
                .FirstOrDefaultAsync(x => 
                    x.ProcessingCostCode == processingCostCode && 
                    x.ProcessingServiceCode == processingServiceCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching entity with ID {Id}", processingServiceCode);
            throw new DefaultException("Error fetching entity");
        }
    }


    public async Task<ProcessingCostServiceDetail?> UpdateTabelaCustoServicoAsync(string code, ProcessingCostServiceDetail entity)
    {
        try
        {
            var existingEntity = await _context.Set<ProcessingCostServiceDetail>()
                .FirstOrDefaultAsync(tc => tc.ProcessingServiceCode == code) ?? 
                                 throw new KeyNotFoundException("Entity not found.");
            _context.Entry(existingEntity).CurrentValues.SetValues(entity);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists("Code", code))
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

