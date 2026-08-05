using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>
/// Naturezas de operação do modo STANDALONE: identidade fiscal em USAGES, efeito de negócio
/// em USAGE_EFFECTS. O LEFT JOIN é o mesmo do modo SAPB1 — muda só a fonte da identidade.
/// </summary>
public class UsageService(IUnitOfWork db, ILogger<UsageService> logger)
    : IUsage
{
    public IQueryable<UsageModel> QueryAll()
    {
        return db.Context.Usages
            .GroupJoin(
                db.Context.UsageEffects,
                usage => usage.Code,
                effect => effect.UsageCode,
                (usage, effects) => new { usage, effects })
            .SelectMany(
                x => x.effects.DefaultIfEmpty(),
                (x, effect) => new UsageModel()
                {
                    Code = x.usage.Code,
                    Name = x.usage.Name,
                    Description = x.usage.Description,
                    CfopOutgoingInState = x.usage.CfopOutgoingInState,
                    CfopOutgoingOutState = x.usage.CfopOutgoingOutState,
                    Inactive = x.usage.Inactive,
                    ContractBalanceEffect = effect != null ? effect.ContractBalanceEffect : 0,
                    ContractValueEffect = effect != null ? effect.ContractValueEffect : 0,
                    RequiresContract = effect != null && effect.RequiresContract,
                    RequiresQuantity = effect == null || effect.RequiresQuantity,
                    RequiresWeight = effect != null && effect.RequiresWeight,
                    IsDefault = effect != null && effect.IsDefault,
                    HasConfiguredEffects = effect != null,
                })
            .AsNoTracking();
    }

    public async Task<UsageModel?> GetByIdAsync(int key)
    {
        try
        {
            return await QueryAll().FirstOrDefaultAsync(x => x.Code == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<IEnumerable<UsageModel>> GetAllAsync()
    {
        return await QueryAll().ToListAsync();
    }

    public async Task<UsageModel> CreateAsync(UsageModel entity)
    {
        UsageEffectWriter.ValidateEffects(entity);

        var usage = new Usage()
        {
            Name = entity.Name,
            Description = entity.Description,
            CfopOutgoingInState = entity.CfopOutgoingInState,
            CfopOutgoingOutState = entity.CfopOutgoingOutState,
            Inactive = entity.Inactive,
        };

        await db.Context.Usages.AddAsync(usage);

        // Precisa do Code gerado antes de gravar o efeito, que é chaveado por ele.
        await db.SaveChangesAsync();

        await UsageEffectWriter.WriteAsync(db, usage.Code, entity);
        await db.SaveChangesAsync();

        entity.Code = usage.Code;
        entity.HasConfiguredEffects = true;

        return entity;
    }

    public async Task<UsageModel?> UpdateAsync(int key, UsageModel entity)
    {
        UsageEffectWriter.ValidateEffects(entity);

        var usage = await db.Context.Usages.FirstOrDefaultAsync(x => x.Code == key);

        if (usage == null)
        {
            return null;
        }

        usage.Name = entity.Name;
        usage.Description = entity.Description;
        usage.CfopOutgoingInState = entity.CfopOutgoingInState;
        usage.CfopOutgoingOutState = entity.CfopOutgoingOutState;
        usage.Inactive = entity.Inactive;

        await UsageEffectWriter.WriteAsync(db, key, entity);

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

            throw new DefaultException("Error updating entity.");
        }

        entity.Code = usage.Code;
        entity.HasConfiguredEffects = true;

        return entity;
    }

    public async Task<bool> DeleteAsync(int key)
    {
        var usage = await db.Context.Usages.FirstOrDefaultAsync(x => x.Code == key);

        if (usage == null)
        {
            return false;
        }

        // Não há FK para USAGES (é cadastro dual-mode), então a integridade referencial
        // mora aqui: o banco não recusaria a exclusão de uma natureza já usada.
        // A natureza é de LINHA, então quem a referencia é o item, não o cabeçalho.
        if (await db.Context.SalesInvoicesItems.AnyAsync(x => x.UsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} já foi utilizada em documento de saída. " +
                "Inative-a em vez de excluir.");
        }

        var effect = await db.Context.UsageEffects.FirstOrDefaultAsync(x => x.UsageCode == key);

        if (effect != null)
        {
            db.Context.UsageEffects.Remove(effect);
        }

        db.Context.Usages.Remove(usage);
        await db.SaveChangesAsync();

        return true;
    }

    private bool EntityExists(int key)
    {
        return db.Context.Usages.Any(x => x.Code == key);
    }
}
