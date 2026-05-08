using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class UserCreateDto
{
    [JsonPropertyName("Username")]
    public required string Username { get; set; } 
    
    [JsonPropertyName("Password")]
    public string? Password { get; set; }
    
    [JsonPropertyName("FullName")]
    public string? FullName { get; set; }
    
    [JsonPropertyName("Email")]
    public string? Email { get; set; }
    
    [JsonPropertyName("IsAdmin")]
    public bool IsAdmin { get; set; }
}