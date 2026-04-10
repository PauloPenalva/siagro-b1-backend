namespace SiagroB1.Domain.Dtos;

public class WarehouseInfo
{
    
    public required string CardCode { get; init; }
    public string? CardFName { get; init; }
    public string? TaxId { get; init; }
    public string? Notes { get; init; }
    public WarehouseAddress? Address { get; init; }
    
}