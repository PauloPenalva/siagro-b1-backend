using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class ItemService(IUnitOfWork db, ILogger<ItemService> logger, IConfiguration configuration,
    DocNumberSequenceService docNumbers) 
    : IItemService
{
    public async Task<ItemModel> CreateAsync(ItemModel entity)
    {
        ValidateFiscalFields(entity);

        var assignment = await docNumbers.AssignAsync(new DocNumberRequest(
            TransactionCode.Item, entity.DocNumberKey, entity.ItemCode, 50,
            code => db.Context.Items.AnyAsync(i => i.ItemCode == code)));
        entity.ItemCode = assignment.Code;

        var item = new Item()
        {
            ItemCode = entity.ItemCode,
            ItemName = entity.ItemName,
            ItmsGrpCod = entity.ItmsGrpCod,
            Enabled = entity.Enabled,
            GoodsOrigin = entity.GoodsOrigin,
            Ncm = NormalizeNcm(entity.Ncm),
            Cest = NormalizeCest(entity.Cest),
        };
            
        await db.Context.Items.AddAsync(item);
        await db.SaveChangesAsync();
        
        return entity;
    }
    
    public IQueryable<ItemModel> QueryAll()
    {
        return QueryAllActive().Where(x => x.ItmsGrpCod == 105);
    }

    public IQueryable<ItemModel> QueryAllActive()
    {
        return db.Context.Items
            .Select(x => new ItemModel()
            {
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                ItmsGrpCod = x.ItmsGrpCod,
                Enabled = x.Enabled,
                GoodsOrigin = x.GoodsOrigin,
                Ncm = x.Ncm,
                Cest = x.Cest,
            })
            .AsNoTracking()
            .Where(x => x.Enabled != null &&
                        x.Enabled.ToUpper() == "SIM");
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<ItemModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<ItemModel>> GetAllAsync()
    {
        throw new NotImplementedException();
    }

    public Task<ItemModel?> GetByIdAsync(string code) => FetchAsync(code, grainOnly: true);

    public Task<ItemModel?> GetActiveByIdAsync(string code) => FetchAsync(code, grainOnly: false);

    private async Task<ItemModel?> FetchAsync(string code, bool grainOnly)
    {
        try
        {
            var query = grainOnly ? QueryAll() : QueryAllActive();
            return await query.FirstOrDefaultAsync(x => x.ItemCode == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public async Task<bool> DeleteAsync(string code)
    {
        var entity = await db.Context.Items.FirstOrDefaultAsync(x => x.ItemCode == code);
        if (entity == null)
        {
            return false;
        }

        db.Context.Items.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }
    
    public async Task<ItemModel?> UpdateAsync(string code, ItemModel entity)
    {
        var item = await db.Context.Set<Item>()
            .FirstOrDefaultAsync(x => x.ItemCode == code);

        if (item == null)
            return null;

        ValidateFiscalFields(entity);

        item.ItemName = entity.ItemName;
        item.ItmsGrpCod = entity.ItmsGrpCod;
        item.Enabled = entity.Enabled;
        item.GoodsOrigin = entity.GoodsOrigin;
        item.Ncm = NormalizeNcm(entity.Ncm);
        item.Cest = NormalizeCest(entity.Cest);
        
        try
        {
            await db.SaveChangesAsync();
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

        return new ItemModel()
        {
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            ItmsGrpCod = item.ItmsGrpCod,
            Enabled = item.Enabled,
            GoodsOrigin = item.GoodsOrigin,
            Ncm = item.Ncm,
            Cest = item.Cest,
        };
    }
    
    /// <summary>Opcionais no cadastro (a MH Agro não emite NF-e); quem exige é o documento.</summary>
    private static void ValidateFiscalFields(ItemModel entity)
    {
        var ncm = NormalizeNcm(entity.Ncm);

        if (ncm is not null && (ncm.Length != 8 || !ncm.All(char.IsDigit)))
            throw new DefaultException("O NCM deve ter 8 dígitos.");

        if (entity.GoodsOrigin is > 8)
            throw new DefaultException("A origem da mercadoria deve estar entre 0 e 8.");

        var cest = NormalizeCest(entity.Cest);

        if (cest is not null && (cest.Length != 7 || !cest.All(char.IsDigit)))
            throw new DefaultException("O CEST deve ter 7 dígitos.");
    }

    private static string? NormalizeNcm(string? ncm) =>
        string.IsNullOrWhiteSpace(ncm) ? null : ncm.Trim();

    /// <summary>Aceita o CEST no formato oficial com pontos (06.005.00) e grava só os dígitos.</summary>
    private static string? NormalizeCest(string? cest) =>
        string.IsNullOrWhiteSpace(cest) ? null : cest.Replace(".", "").Trim();

    private bool EntityExists(string code)
    {
        return db.Context.Items.Any(e => e.ItemCode == code);
    }
}