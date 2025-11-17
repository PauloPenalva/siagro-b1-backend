namespace SiagroB1.Domain.Shared.Base;

public interface IActionEntityDelete<ID>
{
    Task<bool> ExecuteAsync(ID key);
}