namespace SiagroB1.Domain.Shared.Base;

public interface IActionAssociationEntityCreate<T, ID>
    where T : class
{
    Task<T> ExecuteAsync(ID key, T associationEntity);
}