using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class ItemService(IUnitOfWork db, ILogger<ItemService> logger, IConfiguration configuration) 
    : IItemService
{
    public async Task<ItemModel> CreateAsync(ItemModel entity)
    {
        ValidateFiscalFields(entity);

        var item = new Item()
        {
            ItemCode = entity.ItemCode,
            ItemName = entity.ItemName,
            ItmsGrpCod = entity.ItmsGrpCod,
            Enabled = entity.Enabled,
            GoodsOrigin = entity.GoodsOrigin,
            Ncm = NormalizeNcm(entity.Ncm),
        };
            
        await db.Context.Items.AddAsync(item);
        await db.SaveChangesAsync();
        
        return entity;
    }
    
    public IQueryable<ItemModel> QueryAll()
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
            })
            .AsNoTracking()
            .Where(x => x.ItmsGrpCod == 105 && 
                        x.Enabled != null  && 
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

    public async Task<ItemModel?> GetByIdAsync(string code)
    {
        try
        {
            return await db.Context.Items
                .Select(x => new ItemModel()
                {
                    ItemCode = x.ItemCode,
                    ItemName = x.ItemName,
                    ItmsGrpCod = x.ItmsGrpCod,
                    Enabled = x.Enabled,
                    GoodsOrigin = x.GoodsOrigin,
                    Ncm = x.Ncm,
                })
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ItemCode == code && 
                                          x.ItmsGrpCod == 105 &&
                                          x.Enabled != null &&
                                          x.Enabled.ToUpper() == "SIM");
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
    }

    private static string? NormalizeNcm(string? ncm) =>
        string.IsNullOrWhiteSpace(ncm) ? null : ncm.Trim();

    private bool EntityExists(string code)
    {
        return db.Context.Items.Any(e => e.ItemCode == code);
    }
}