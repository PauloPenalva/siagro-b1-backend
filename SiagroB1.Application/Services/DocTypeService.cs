using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class DocTypeService(AppDbContext context) 
{
    public async Task<DocType> CreateAsync(DocType entity)
    {
        var existingBranch = await context.DocTypes
            .FirstOrDefaultAsync(b => b.Code == entity.Code);
        
        if (existingBranch != null)
        {
            throw new DefaultException($"DocType {entity.Code} already exists.");
        }
        
        await context.DocTypes.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(string code)
    {
        var entity = await context.DocTypes.FindAsync(code);
        if (entity == null)
        {
            return false;
        }

        context.DocTypes.Remove(entity);
        await context.SaveChangesAsync();
        return true;
    }

    public IQueryable<DocType> QueryAll()
    {
        return context.DocTypes.AsNoTracking();
    }

    public async Task<IEnumerable<DocType>> GetAllAsync()
    {
        return await context.DocTypes.ToListAsync();
    }

    public async Task<DocType?> GetByIdAsync(string code)
    {
        return await context.DocTypes.FindAsync(code);
    }

    public async Task<DocType?> UpdateAsync(string code, DocType entity)
    {
        context.Entry(entity).State = EntityState.Modified;

        try
        {
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
                throw new DefaultException("Error updating entity.");
            }
        }

        return entity;
    }
    
    private bool EntityExists(string code)
    {
        return context.DocTypes.Any(e => e.Code == code);
    }
}
