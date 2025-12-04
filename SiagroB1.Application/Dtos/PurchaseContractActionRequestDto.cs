using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class PurchaseContractActionRequestDto
{
    [JsonPropertyName("Key")] 
    public Guid Key { get; set; }
}