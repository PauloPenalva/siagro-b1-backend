namespace SiagroB1.Security.Dtos;

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;

    /// <summary>Empresa escolhida na tela de login. Nulo só entra quando há uma única empresa cadastrada.</summary>
    public string? CompanyCode { get; set; }
}