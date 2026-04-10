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
        var item = new Item()
        {
            ItemCode = entity.ItemCode,
            ItemName = entity.ItemName,
            ItmsGrpCod = entity.ItmsGrpCod,
            Enabled = entity.Enabled,
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

        item.ItemName = entity.ItemName;
        item.ItmsGrpCod = entity.ItmsGrpCod;
        item.Enabled = entity.Enabled;
        
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
        };
    }
    
    private bool EntityExists(string code)
    {
        return db.Context.Items.Any(e => e.ItemCode == code);
    }
}