using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Model;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class UnitOfMeasureService(AppDbContext context, ILogger<UnitOfMeasureService> logger) 
    : IUnitOfMeasureService
{
    public  async Task<UnitOfMeasureModel> CreateAsync(UnitOfMeasureModel model)
    {
        if (string.IsNullOrEmpty(model.Code))
            throw new ArgumentException($"Chave não informada.");
        
        if (EntityExists(model.Code))
            throw new ArgumentException($"Unidade de Medida com código '{model.Code}' já existe.");

        try
        {

            var entity = new SiagroB1.Domain.Entities.UnitOfMeasure
            {
                Code = model.Code,
                Description = model.Description,  
            };
            
            await context.UnitsOfMeasure.AddAsync(entity);
            await context.SaveChangesAsync();
            return model;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating model.");
            throw;
        }
    }
    
    public async Task<bool> DeleteAsync(string code)
    {
        try
        {
            var entity = await context.UnitsOfMeasure.FindAsync(code);
            if (entity == null)
            {
                return false;
            }

            context.UnitsOfMeasure.Remove(entity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting entity.");
            throw new DefaultException("Error deleting entity.");
        }
    }

    public IQueryable<UnitOfMeasureModel> QueryAll()
    {
        return context.UnitsOfMeasure
            .AsNoTracking()
            .Select(x => new UnitOfMeasureModel
            {
                Code = x.Code,
                Description = x.Description,
                Locked =  x.Locked ?? "N",
            });
    }

    public async Task<IEnumerable<UnitOfMeasureModel>> GetAllAsync()
    {
        return await context.UnitsOfMeasure
            .Select(x => new UnitOfMeasureModel
            {
                Code = x.Code,
                Description = x.Description,
                Locked = x.Locked ?? "N",
            })
            .ToListAsync();
    }

    public async Task<UnitOfMeasureModel?> GetByIdAsync(string code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);

            return await context.UnitsOfMeasure
                .Select(x => new UnitOfMeasureModel
                {
                    Code = x.Code,
                    Description = x.Description,
                    Locked = x.Locked ?? "N",
                })
                .FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }


    public async Task<UnitOfMeasureModel?> UpdateAsync(string code, UnitOfMeasureModel model)
    {
        try
        {
            var entity = await context.UnitsOfMeasure.FindAsync(code);
            if (entity == null) throw  new NotFoundException("Entity not found.");
            
            context.Entry(entity).State = EntityState.Modified;
            entity.Description = model.Description;
            
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(code))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                throw new DefaultException("Error updating model due to concurrency issues.");
            }
        }

        return model;
    }

        
    private bool EntityExists(string key)
    {
        return context.UnitsOfMeasure.Any(x => x.Code == key);
    }
    
}
