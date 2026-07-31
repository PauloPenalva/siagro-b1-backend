using Microsoft.Extensions.Configuration;

namespace SiagroB1.Security.Shared;

/// <summary>
/// Regra de senha, aplicada em todo lugar onde uma senha é definida (criação de usuário,
/// redefinição por e-mail e troca no perfil). A tela repete a validação, mas quem manda é aqui —
/// os endpoints de senha são públicos.
///
/// O padrão é permissivo de propósito: acompanha o cadastro do SAP Business One, onde as senhas
/// em uso são curtas e só de dígitos. Exigir mais aqui do que o SAP exige lá deixaria parte dos
/// usuários sem conseguir repetir a senha que já usam.
///
/// Configurável por ambiente, sem mexer em código:
/// <code>
/// "Security": {
///   "PasswordPolicy": { "MinimumLength": 4, "RequireLetterAndDigit": false }
/// }
/// </code>
/// </summary>
public class PasswordPolicy(IConfiguration configuration)
{
    /// <summary>
    /// Piso absoluto. Existe para que uma configuração errada (0, negativo) não passe a aceitar
    /// senha vazia — hash nulo/vazio é o que marca "usuário ainda sem senha" no login.
    /// </summary>
    private const int AbsoluteMinimumLength = 1;

    private const int DefaultMinimumLength = 4;

    public int MinimumLength =>
        Math.Max(
            configuration.GetValue("Security:PasswordPolicy:MinimumLength", DefaultMinimumLength),
            AbsoluteMinimumLength);

    public bool RequireLetterAndDigit =>
        configuration.GetValue("Security:PasswordPolicy:RequireLetterAndDigit", false);

    /// <summary>
    /// Texto exibido nas telas de senha. Vem do servidor para não divergir da regra aplicada -
    /// um aviso fixo na tela viraria mentira assim que a configuração mudasse.
    /// </summary>
    public string Description =>
        RequireLetterAndDigit
            ? $"Mínimo de {MinimumLength} caracteres, com ao menos uma letra e um número."
            : $"Mínimo de {MinimumLength} caracteres.";

    public bool IsValid(string? password, out string error)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            error = $"A senha deve ter no mínimo {MinimumLength} caracteres.";
            return false;
        }

        if (RequireLetterAndDigit && (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)))
        {
            error = "A senha deve conter ao menos uma letra e um número.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
