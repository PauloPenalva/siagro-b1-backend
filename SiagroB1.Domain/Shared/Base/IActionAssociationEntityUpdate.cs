namespace SiagroB1.Domain.Shared.Base;

public interface IActionAssociationEntityUpdate<T, ID, ID2>
    where T : class
{
    Task<T?> ExecuteAsync(ID key, ID2 associationKey, T associationEntity);
}