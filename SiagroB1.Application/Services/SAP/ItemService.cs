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
        // GAC-1196: o inativo continua na consulta (documento antigo resolve o nome); quem
        // ESCOLHE o produto filtra/confere `Inactive`.
        var inactiveCodes = context.Items
            .Where(SapRecordActivity.IsInactive<Domain.Entities.SAP.Item>(DateTime.Today))
            .Select(i => i.ItemCode);

        return context.Items
            .Select(x => new ItemModel()
            {
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                ItmsGrpCod = x.ItmsGrpCod,
                Enabled = x.Enabled,
                Inactive = inactiveCodes.Contains(x.ItemCode),
            })
            .AsNoTracking()
            // GAC-1209: só o campo U_YKT_Integra_Siagro decide o que integra (feijão está em outro grupo)
            .Where(x => x.Enabled != null  &&
                        x.Enabled.ToUpper() == "SIM");
    }
    
    /// <summary>SAPB1: nada muda, "outra compra" só existe em STANDALONE.</summary>
    public IQueryable<ItemModel> QueryAllActive() => QueryAll();

    public Task<ItemModel?> GetActiveByIdAsync(string code) => GetByIdAsync(code);

    public async Task<ItemModel?> GetByIdAsync(string code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);
            return await QueryAll().FirstOrDefaultAsync(x => x.ItemCode == code);
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