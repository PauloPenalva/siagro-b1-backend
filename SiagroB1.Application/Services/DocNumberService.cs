using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class DocNumberService(IUnitOfWork db)
{
    public async Task<DocNumber> CreateAsync(DocNumber entity)
    {
        var existingDocNumber = await db.Context.DocNumbers
            .FirstOrDefaultAsync(b => b.TransactionCode == entity.TransactionCode &&
                                      b.Name.ToUpper().Trim() == entity.Name.ToUpper().Trim());
        
        if (existingDocNumber != null)
        {
            throw new DefaultException($"DocNumber already exists.");
        }
        
        await db.Context.DocNumbers.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid key)
    {
        var entity = await db.Context.DocNumbers.FindAsync(key);
        if (entity == null)
        {
            return false;
        }

        db.Context.DocNumbers.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public IQueryable<DocNumber> QueryAll()
    {
        return db.Context.DocNumbers.AsNoTracking();
    }

    public async Task<IEnumerable<DocNumber>> GetAllAsync()
    {
        return await db.Context.DocNumbers.ToListAsync();
    }

    public async Task<DocNumber?> GetByIdAsync(Guid key)
    {
        return await db.Context.DocNumbers.FindAsync(key);
    }

    public async Task<DocNumber?> UpdateAsync(Guid key, DocNumber entity)
    {
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
    
    private bool EntityExists(Guid key)
    {
        return db.Context.DocNumbers.Any(e => e.Key == key);
    }
}