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

    /// <summary>
    /// Regra de senha vigente, em texto, para as telas que pedem senha.
    ///
    /// Vai no login E no /status de propósito: o login não é seguido de uma consulta ao /status
    /// (a resposta dele já traz o usuário), então publicar só num dos dois deixaria a regra
    /// ausente na primeira sessão ou perdida no primeiro F5.
    /// </summary>
    public string? PasswordRequirements { get; set; }
}