using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class ItemService(SapErpDbContext context, ILogger<ItemService> logger, IConfiguration configuration) 
    : BaseService<Item, string>(context, logger), IItemService
{
    public virtual IQueryable<Item> QueryAll()
    {
        return context.Items
            .AsNoTracking()
            .Where(x => x.ItmsGrpCod == 105);
    }
    
    public virtual async Task<Item?> GetByIdAsync(string code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);
            return await context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ItemCode == code && x.ItmsGrpCod == 105);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }
}