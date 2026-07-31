using System.Security.Cryptography;

namespace SiagroB1.Security.Shared;

/// <summary>
/// Hash de senha em PBKDF2-SHA256 com salt por usuário.
///
/// O formato gravado é <c>PBKDF2$&lt;iterações&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;</c>: o
/// número de iterações viaja junto do hash para que aumentá-lo no futuro não invalide as senhas
/// já existentes.
///
/// A base instalada tem hashes no formato antigo (SHA-256 sem salt, <see cref="Utils.HashPassword"/>).
/// <see cref="Verify"/> aceita os dois e sinaliza, por <c>needsUpgrade</c>, quando o chamador deve
/// regravar o hash no formato novo.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "PBKDF2";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, Iterations);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Confere a senha contra o hash guardado.
    /// </summary>
    /// <param name="needsUpgrade">
    /// <c>true</c> quando a senha confere mas o hash está no formato antigo — o chamador deve
    /// regravar com <see cref="Hash"/>.
    /// </param>
    public static bool Verify(string? storedHash, string password, out bool needsUpgrade)
    {
        needsUpgrade = false;

        // Usuário criado pelo sync do SAP nasce sem hash: não autentica com senha nenhuma.
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        if (!storedHash.StartsWith(Prefix + "$", StringComparison.Ordinal))
        {
            // Formato legado: SHA-256 sem salt.
            if (!FixedTimeEquals(storedHash, Utils.HashPassword(password)))
            {
                return false;
            }

            needsUpgrade = true;
            return true;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations <= 0 ||
            !TryFromBase64(parts[2], out var salt) ||
            !TryFromBase64(parts[3], out var expected))
        {
            // Hash corrompido no banco: recusa o login em vez de derrubar o endpoint.
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int size = HashSize) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, size);

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var buffer = new byte[((value.Length + 3) / 4) * 3];
        if (!Convert.TryFromBase64String(value, buffer, out var written) || written == 0)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(left),
            System.Text.Encoding.UTF8.GetBytes(right));
}
