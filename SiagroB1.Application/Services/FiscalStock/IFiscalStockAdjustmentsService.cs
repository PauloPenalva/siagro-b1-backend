using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.FiscalStock;

public interface IFiscalStockAdjustmentsService
{
    IQueryable<FiscalStockAdjustment> QueryAll();

    Task<FiscalStockAdjustment?> GetByKeyAsync(Guid key);

    Task<FiscalStockAdjustment> CreateAsync(FiscalStockAdjustment entity, string userName);

    Task<FiscalStockAdjustment?> UpdateAsync(Guid key, FiscalStockAdjustment entity);

    Task<bool> DeleteAsync(Guid key);
}
