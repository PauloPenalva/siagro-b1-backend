namespace SiagroB1.Security.Dtos;

/// <summary>Pedido de recuperação: o usuário informa o login ou o e-mail cadastrado.</summary>
public class ForgotPasswordRequest
{
    public string UsernameOrEmail { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
