namespace SiagroB1.Domain.Shared.Base;

public interface IActionEntityUpdate<T, ID>
    where T : class
{
    Task<T?> ExecuteAsync(ID key, T entity);
}