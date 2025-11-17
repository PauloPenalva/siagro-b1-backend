namespace SiagroB1.Domain.Shared.Base;

public interface IActionAssociationEntityDelete<ID, ID2>
{
    Task<bool> ExecuteAsync(ID key, ID2 associationKey);
}