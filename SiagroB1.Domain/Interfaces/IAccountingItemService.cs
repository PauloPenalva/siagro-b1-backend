using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IAccountingItemService
{
    Task<IEnumerable<AccountingItemModel>> GetAllAsync();
    Task<AccountingItemModel?> GetByIdAsync(string code);
    Task<AccountingItemModel> CreateAsync(AccountingItemModel entity);
    Task<AccountingItemModel?> UpdateAsync(string code, AccountingItemModel entity);
    Task<bool> DeleteAsync(string code);
    IQueryable<AccountingItemModel> QueryAll();
    Task<bool> DeleteAsyncWithTransaction(string code, Func<AccountingItemModel, Task>? preDeleteAction = null);
}
