using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class PurchaseContractActionRequestDto
{
    [JsonPropertyName("Key")] 
    public Guid Key { get; set; }
}