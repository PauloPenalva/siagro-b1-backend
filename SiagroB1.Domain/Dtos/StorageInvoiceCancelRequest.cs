namespace SiagroB1.Domain.Dtos;

public class StorageInvoiceCancelRequest
{
    public Guid StorageInvoiceKey { get; set; }
    public required string Reason { get; set; }
}