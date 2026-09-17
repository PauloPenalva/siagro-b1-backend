namespace SiagroB1.Domain.Dtos;

public class WarehouseReconciliationReleaseLineDto
{
    public Guid ShipmentReleaseKey { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string? PurchaseContractCode { get; set; }
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public decimal Quantity { get; set; }
    public string? PurchaseTransactionCode { get; set; }
    public string? LossTransactionCode { get; set; }
}
