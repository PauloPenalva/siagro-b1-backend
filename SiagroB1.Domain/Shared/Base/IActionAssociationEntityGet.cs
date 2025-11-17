namespace SiagroB1.Domain.Shared.Base;

public interface IActionAssociationEntityGet<T, ID, ID2>
{
    Task<T?> GetByIdAsync(ID key, ID2 associationKey);

    IQueryable<T> QueryAll(ID key);
}