using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class UserDto
{
    [JsonPropertyName("Id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("Username")]
    public required string Username { get; set; } 
    
    [JsonPropertyName("FullName")]
    public string? FullName { get; set; }
    
    [JsonPropertyName("Email")]
    public string? Email { get; set; }
    
    [JsonPropertyName("IsAdmin")]
    public bool IsAdmin { get; set; }
    
    [JsonPropertyName("IsActive")]
    public bool IsActive { get; set; }
}