using System.Security.Claims;

namespace SiagroB1.Security.Dtos;

public class LoginResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UserInfo? User { get; set; }
    public string? SessionId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public List<Claim>? Claims { get; set; }
}