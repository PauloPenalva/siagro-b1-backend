using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
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
        
        await ValidateRulesAsync(entity, null);
        await UnsetOtherDefaultsAsync(entity);

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

        if (entity.IsManual)
        {
            throw new ApplicationException("A série Manual não pode ser excluída.");
        }

        if (entity.Default)
        {
            throw new ApplicationException("A série padrão não pode ser excluída. Marque outra série como padrão antes.");
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
        // Lê o valor GRAVADO por uma consulta sem rastreamento: no PATCH a entidade recebida já está
        // rastreada e patcheada, então comparar com ela mesma nunca detectaria a mudança.
        var stored = await db.Context.DocNumbers.AsNoTracking().FirstOrDefaultAsync(d => d.Key == key);
        if (stored == null)
        {
            throw new KeyNotFoundException("Entity not found.");
        }

        await ValidateRulesAsync(entity, stored);
        await UnsetOtherDefaultsAsync(entity);

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

    private async Task ValidateRulesAsync(DocNumber entity, DocNumber? stored)
    {
        if (stored != null && stored.IsManual != entity.IsManual)
        {
            throw new ApplicationException("O tipo da série (Automático/Manual) não pode ser alterado.");
        }

        if (stored != null && stored.IsManual &&
            (stored.Name != entity.Name || stored.TransactionCode != entity.TransactionCode))
        {
            throw new ApplicationException("A série Manual não pode ser renomeada nem mudar de tipo de documento.");
        }

        if (entity.IsManual)
        {
            if (!DocNumberSequenceService.AllowsManual(entity.TransactionCode))
            {
                throw new ApplicationException("Este tipo de documento não admite série Manual.");
            }

            if (entity.Default)
            {
                throw new ApplicationException("A série Manual não pode ser a padrão.");
            }

            var hasOtherManual = await db.Context.DocNumbers.AsNoTracking()
                .AnyAsync(d => d.TransactionCode == entity.TransactionCode && d.IsManual && d.Key != entity.Key);
            if (hasOtherManual)
            {
                throw new ApplicationException("Já existe uma série Manual para este tipo de documento.");
            }
        }

        if (entity.Default && entity.Inactive)
        {
            throw new ApplicationException("A série padrão não pode ser inativada.");
        }
    }

    /// <summary>Só uma série padrão por tipo: desmarca as demais no mesmo SaveChanges do salvamento.</summary>
    private async Task UnsetOtherDefaultsAsync(DocNumber entity)
    {
        if (!entity.Default)
        {
            return;
        }

        var others = await db.Context.DocNumbers
            .Where(d => d.TransactionCode == entity.TransactionCode && d.Default && d.Key != entity.Key)
            .ToListAsync();
        foreach (var other in others)
        {
            other.Default = false;
        }
    }
}
