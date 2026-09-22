using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Linha da aba "Minutas": sem BodyHtml/PlaceholdersJson (pesados; a tela pede pela function GetBody).</summary>
public class ContractDraftDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("ContractCode")] public string? ContractCode { get; set; }
    [JsonPropertyName("Sequence")] public int Sequence { get; set; }
    [JsonPropertyName("TemplateKey")] public Guid TemplateKey { get; set; }
    [JsonPropertyName("TemplateName")] public string? TemplateName { get; set; }
    [JsonPropertyName("DraftType")] public ContractDraftType DraftType { get; set; }
    [JsonPropertyName("Description")] public string? Description { get; set; }
    [JsonPropertyName("Status")] public ContractDraftStatus Status { get; set; }
    [JsonPropertyName("Provider")] public string? Provider { get; set; }
    [JsonPropertyName("SentAt")] public DateTime? SentAt { get; set; }
    [JsonPropertyName("SignedAt")] public DateTime? SignedAt { get; set; }
    [JsonPropertyName("LastError")] public string? LastError { get; set; }
    [JsonPropertyName("SignedAttachmentKey")] public Guid? SignedAttachmentKey { get; set; }
    [JsonPropertyName("CreatedAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("CreatedBy")] public string? CreatedBy { get; set; }
    [JsonPropertyName("Signers")] public List<ContractDraftSignerDto> Signers { get; set; } = [];
}
