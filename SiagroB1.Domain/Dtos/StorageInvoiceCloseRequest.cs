namespace SiagroB1.Domain.Dtos;

public class StorageInvoiceCloseRequest
{
    public Guid DocNumberKey { get; set; }
    /// <summary>Número digitado, usado só quando a série escolhida é manual.</summary>
    public string? Code { get; set; }
    public required string StorageAddressCode { get; set; }
    public required DateTime ClosingDate { get; set; }
    public required DateTime PeriodStart { get; set; }
    public required DateTime PeriodEnd { get; set; }
    public string? Notes { get; set; }
    public bool IncludeUnpricedItems { get; set; } = false;
}