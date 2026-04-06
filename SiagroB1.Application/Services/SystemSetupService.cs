using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class SystemSetupService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource
    )
{
    public async Task<SystemSetup> CreateAsync(SystemSetup entity)
    {
        var existingSetup = await db.Context.SystemSetup
            .FirstOrDefaultAsync(b => b.Code == entity.Code);
        
        if (existingSetup != null)
        {
            throw new DefaultException($"Setup code already exists.");
        }

        var hasActiveSetup = db.Context.SystemSetup.Any(x => x.IsActive && x.Code != entity.Code);
        if (hasActiveSetup && entity.IsActive)
            throw new BusinessException(resource["EXISTING_SETUP_ACTIVE"]);
        
        await db.Context.SystemSetup.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var entity = await db.Context.SystemSetup.FindAsync(key);
        if (entity == null)
        {
            return false;
        }

        db.Context.SystemSetup.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public IQueryable<SystemSetup> QueryAll()
    {
        return db.Context.SystemSetup.AsNoTracking();
    }

    public async Task<IEnumerable<SystemSetup>> GetAllAsync()
    {
        return await db.Context.SystemSetup.ToListAsync();
    }

    public async Task<SystemSetup?> GetByIdAsync(string key)
    {
        return await db.Context.SystemSetup.FindAsync(key);
    }

    public async Task<SystemSetup?> UpdateAsync(string key, SystemSetup entity)
    {
        var hasActiveSetup = db.Context.SystemSetup.Any(x => x.IsActive && x.Code != entity.Code);
        if (hasActiveSetup && entity.IsActive)
            throw new BusinessException(resource["EXISTING_SETUP_ACTIVE"]);
        
        db.Context.Entry(entity).State = EntityState.Modified;
        
        try
        {
            await db.SaveChangesAsync();
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
    
    private bool EntityExists(string key)
    {
        return db.Context.SystemSetup.Any(e => e.Code == key);
    }
}