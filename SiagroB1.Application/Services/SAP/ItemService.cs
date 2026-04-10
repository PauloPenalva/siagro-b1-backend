using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class ItemService(SapErpDbContext context, ILogger<ItemService> logger, IConfiguration configuration) 
    : IItemService
{
    public IQueryable<ItemModel> QueryAll()
    {
        return context.Items
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
    
    public async Task<ItemModel?> GetByIdAsync(string code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);
            return await context.Items
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
    
    public Task<bool> DeleteAsync(string code)
    {
        throw new NotImplementedException();
    }
    
    public Task<bool> DeleteAsyncWithTransaction(string code, Func<ItemModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<ItemModel>> GetAllAsync()
    {
        throw new NotImplementedException();
    }
    
    public Task<ItemModel> CreateAsync(ItemModel entity)
    {
        throw new NotImplementedException();
    }

    public Task<ItemModel?> UpdateAsync(string code, ItemModel entity)
    {
        throw new NotImplementedException();
    }
}