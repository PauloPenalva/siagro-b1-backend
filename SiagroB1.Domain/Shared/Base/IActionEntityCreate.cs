namespace SiagroB1.Domain.Shared.Base;

public interface IActionEntityCreate<T>
    where T : class
{
    Task<T> ExecuteAsync(T entity);
}