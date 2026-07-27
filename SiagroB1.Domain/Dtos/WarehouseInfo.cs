namespace SiagroB1.Domain.Dtos;

public class WarehouseInfo
{
    
    public required string CardCode { get; init; }

    /// <summary>Razão social. Usada quando o SAP não tem <see cref="CardFName"/>.</summary>
    public string? CardName { get; init; }

    /// <summary>Nome fantasia (<c>OCRD.AliasName</c>), opcional no SAP.</summary>
    public string? CardFName { get; init; }
    public string? TaxId { get; init; }
    public string? Notes { get; init; }
    public WarehouseAddress? Address { get; init; }
    
}