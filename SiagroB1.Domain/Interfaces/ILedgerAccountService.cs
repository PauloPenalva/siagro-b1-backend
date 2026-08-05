using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface ILedgerAccountService
{
    Task<IEnumerable<LedgerAccountModel>> GetAllAsync();
    Task<LedgerAccountModel?> GetByIdAsync(string code);
    Task<LedgerAccountModel> CreateAsync(LedgerAccountModel entity);
    Task<LedgerAccountModel?> UpdateAsync(string code, LedgerAccountModel entity);
    Task<bool> DeleteAsync(string code);
    IQueryable<LedgerAccountModel> QueryAll();
    Task<bool> DeleteAsyncWithTransaction(string code, Func<LedgerAccountModel, Task>? preDeleteAction = null);
}
