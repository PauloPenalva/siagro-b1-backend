using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class SalesContractAttachmentsDto
{
    [JsonPropertyName("Key")]
    public Guid? Key { get; set; }
    
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("FileName")]
    public string? FileName { get; set; }
    
    [JsonPropertyName("CreatedBy")]
    public string? CreatedBy { get; set; }
    
    [JsonPropertyName("CreatedAt")]
    public DateTime? CreatedAt { get; set; }
    
    
}