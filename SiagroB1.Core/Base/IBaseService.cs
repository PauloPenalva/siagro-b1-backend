namespace SiagroB1.Core.Base
{
    public interface IBaseService<T, ID> where T : class
    {
        Task<IEnumerable<T>> GetAllAsync();
        Task<T?> GetByIdAsync(ID id);
        Task<T> CreateAsync(T entity);
        Task<T?> UpdateAsync(ID id, T entity);
        Task<bool> DeleteAsync(ID id);
        IQueryable<T> QueryAll();
        Task<bool> DeleteAsyncWithTransaction(object id, Func<T, Task>? preDeleteAction = null);
    }
}