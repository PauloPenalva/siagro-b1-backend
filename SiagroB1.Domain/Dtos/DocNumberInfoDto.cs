using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class DocNumberInfoDto
{
    [JsonPropertyName("Key")]
    public Guid Key { get; set; }
    
    [JsonPropertyName("Default")]
    public bool Default { get; set; }
}