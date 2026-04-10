
using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IItemService
{
    Task<IEnumerable<ItemModel>> GetAllAsync();
    Task<ItemModel?> GetByIdAsync(string code);
    Task<ItemModel> CreateAsync(ItemModel entity);
    Task<ItemModel?> UpdateAsync(string code, ItemModel entity);
    Task<bool> DeleteAsync(string code);
    IQueryable<ItemModel> QueryAll();
    Task<bool> DeleteAsyncWithTransaction(string code, Func<ItemModel, Task>? preDeleteAction = null);
}