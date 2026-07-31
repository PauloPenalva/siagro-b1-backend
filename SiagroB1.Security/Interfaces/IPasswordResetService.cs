namespace SiagroB1.Security.Interfaces;

/// <param name="Success">Falso quando o token não vale mais ou a senha não atende à política.</param>
public record PasswordResetResult(bool Success, string Message);

/// <summary>
/// Recuperação de senha por e-mail: pedido, validação do token e redefinição.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Regra de senha vigente, em texto. A tela de redefinição a exibe em vez de um aviso fixo:
    /// a política é configurável por ambiente, e um texto no XML viraria mentira ao mudá-la.
    /// </summary>
    string PasswordRequirements { get; }

    /// <summary>
    /// Registra o pedido e envia o link por e-mail.
    ///
    /// Não devolve nada de propósito: a resposta ao usuário é sempre a mesma, exista ou não a
    /// conta. Revelar a diferença transformaria o endpoint num verificador de usuários válidos.
    /// </summary>
    Task RequestAsync(string usernameOrEmail, string? requestIp, CancellationToken ct = default);

    /// <summary>Diz se o token ainda pode ser usado - a tela de redefinição consulta antes de abrir o formulário.</summary>
    Task<bool> ValidateAsync(string token, CancellationToken ct = default);

    Task<PasswordResetResult> ResetAsync(string token, string newPassword, CancellationToken ct = default);
}
