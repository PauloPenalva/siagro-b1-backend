using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.FiscalInventory;

public interface IFiscalInventoryOpeningBalancesService
{
    IQueryable<FiscalInventoryOpeningBalance> QueryAll();

    Task<FiscalInventoryOpeningBalance?> GetByKeyAsync(Guid key);

    Task<FiscalInventoryOpeningBalance> CreateAsync(FiscalInventoryOpeningBalance entity);

    Task<FiscalInventoryOpeningBalance?> UpdateAsync(Guid key, FiscalInventoryOpeningBalance entity);

    Task<bool> DeleteAsync(Guid key);
}
