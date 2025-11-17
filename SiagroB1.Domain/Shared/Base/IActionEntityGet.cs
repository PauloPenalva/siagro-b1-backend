namespace SiagroB1.Domain.Shared.Base;

public interface IActionEntityGet<T, ID>
{
    Task<T?> GetByIdAsync(ID key);

    IQueryable<T> QueryAll();
}