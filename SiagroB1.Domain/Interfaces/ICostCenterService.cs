using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface ICostCenterService
{
    Task<IEnumerable<CostCenterModel>> GetAllAsync();
    Task<CostCenterModel?> GetByIdAsync(string code);
    Task<CostCenterModel> CreateAsync(CostCenterModel entity);
    Task<CostCenterModel?> UpdateAsync(string code, CostCenterModel entity);
    Task<bool> DeleteAsync(string code);
    IQueryable<CostCenterModel> QueryAll();
    Task<bool> DeleteAsyncWithTransaction(string code, Func<CostCenterModel, Task>? preDeleteAction = null);
}
