namespace SiagroB1.Domain.Shared.Base;

public interface IBaseService<T, ID> where T : class
{
    Task<IEnumerable<T>> GetAllAsync();
    Task<T?> GetByIdAsync(ID code);
    Task<T> CreateAsync(T entity);
    Task<T?> UpdateAsync(ID code, T entity);
    Task<bool> DeleteAsync(ID code);
    IQueryable<T> QueryAll();
    Task<bool> DeleteAsyncWithTransaction(ID id, Func<T, Task>? preDeleteAction = null);
}
