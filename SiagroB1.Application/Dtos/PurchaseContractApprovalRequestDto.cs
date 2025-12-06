using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class PurchaseContractApprovalRequestDto
{
    [JsonPropertyName("Key")] 
    public Guid Key { get; set; }
    
    [JsonPropertyName("ApprovalComments")]
    public string? ApprovalComments { get; set; }
}