namespace SiagroB1.Core.Base
{
    public interface IBaseService<T, ID> where T : class
    {
        Task<IEnumerable<T>> GetAllAsync();
        Task<T?> GetByIdAsync(ID key);
        Task<T> CreateAsync(T entity);
        Task<T?> UpdateAsync(ID key, T entity);
        Task<bool> DeleteAsync(ID key);
        IQueryable<T> QueryAll();
        Task<bool> DeleteAsyncWithTransaction(ID id, Func<T, Task>? preDeleteAction = null);
    }
}