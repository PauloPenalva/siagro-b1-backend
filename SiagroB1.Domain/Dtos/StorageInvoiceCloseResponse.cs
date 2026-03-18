using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class StorageInvoiceCloseResponse
{
    [JsonPropertyName("Key")]
    public Guid Key { get; set; }
}