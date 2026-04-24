using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class TruckScaleService(IUnitOfWork db, ILogger<TruckScaleService> logger) 
{
    public async Task<TruckScale> CreateAsync(TruckScale entity)
    {
        var existingEntity = await db.Context.TruckScales
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Code == entity.Code);
        
        if (existingEntity != null)
        {
            throw new DefaultException($"Balança {entity.Code} já cadastrada.");
        }
        
        await db.Context.TruckScales.AddAsync(entity);
        await db.Context.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var entity = await db.Context.TruckScales.FindAsync(key);
        if (entity == null)
        {
            return false;
        }

        db.Context.TruckScales.Remove(entity);
        await db.Context.SaveChangesAsync();
        return true;
    }

    public IQueryable<TruckScale> QueryAll()
    {
        return db.Context.TruckScales.AsNoTracking();
    }

    public async Task<IEnumerable<TruckScale>> GetAllAsync()
    {
        return await db.Context.TruckScales.ToListAsync();
    }

    public async Task<TruckScale?> GetByIdAsync(string key)
    {
        return await db.Context.TruckScales.FindAsync(key);
    }

    public async Task<TruckScale?> UpdateAsync(string key, TruckScale entity)
    {
        db.Context.Entry(entity).State = EntityState.Modified;

        try
        {
            await db.Context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(key))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                throw new DefaultException("Error updating entity.");
            }
        }

        return entity;
    }
    
    private bool EntityExists(string code)
    {
        return db.Context.TruckScales.Any(e => e.Code == code);
    }
}