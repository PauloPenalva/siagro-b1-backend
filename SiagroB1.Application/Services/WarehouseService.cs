using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class WarehouseService(AppDbContext context, ILogger<WarehouseService> logger) 
    : IWarehouseService
{
    public  async Task<WarehouseModel> CreateAsync(WarehouseModel model)
    {
        if (EntityExists(model.Code))
            throw new ArgumentException($"Armazém com código '{model.Code}' já existe.");

        try
        {
            var entity = new Warehouse
            {
                Code = model.Code,
                Name = model.Name,
                TaxId = model.TaxId
            };
            
            await context.Warehouses.AddAsync(entity);
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
            var entity = await context.Warehouses.FindAsync(code);
            if (entity == null)
            {
                return false;
            }

            context.Warehouses.Remove(entity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting entity.");
            throw new DefaultException("Error deleting entity.");
        }
    }

    public IQueryable<WarehouseModel> QueryAll()
    {
        return context.Warehouses
            .AsNoTracking()
            .Select(x => new WarehouseModel
            {
                Code = x.Code,
                Name = x.Name,
                TaxId = x.TaxId,
            });
    }

    public async Task<IEnumerable<WarehouseModel>> GetAllAsync()
    {
        return await context.Warehouses
            .Select(x => new WarehouseModel
            {
                Code = x.Code,
                Name = x.Name,
                TaxId = x.TaxId,
            })
            .ToListAsync();
    }

    public async Task<WarehouseModel?> GetByIdAsync(string code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);

            return await context.Warehouses
                .Select(x => new WarehouseModel
                {
                    Code = x.Code,
                    Name = x.Name,
                    TaxId = x.TaxId,
                })
                .FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }


    public async Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model)
    {
        try
        {
            var entity = await context.Warehouses.FindAsync(code);
            if (entity == null) throw  new NotFoundException("Entity not found.");
            
            context.Entry(entity).State = EntityState.Modified;
            entity.Name = model.Name;
            entity.TaxId = model.TaxId;
            
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
        return context.Warehouses.Any(x => x.Code == key);
    }
}