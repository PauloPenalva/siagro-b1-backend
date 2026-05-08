using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class UserUpdateDto : UserCreateDto
{
    [JsonPropertyName("Id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("IsActive")]
    public bool IsActive { get; set; }
}