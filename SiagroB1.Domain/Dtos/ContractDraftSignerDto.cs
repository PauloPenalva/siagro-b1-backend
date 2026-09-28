using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

public class ContractDraftSignerDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Side")] public SignerSide Side { get; set; }
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("Email")] public string? Email { get; set; }
    [JsonPropertyName("Role")] public SignatoryRole Role { get; set; }
    [JsonPropertyName("Order")] public int Order { get; set; }
    [JsonPropertyName("Status")] public SignerStatus Status { get; set; }
    [JsonPropertyName("SignedAt")] public DateTime? SignedAt { get; set; }
    [JsonPropertyName("LastMessage")] public string? LastMessage { get; set; }
}
