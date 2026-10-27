using System.Text.RegularExpressions;

namespace SiagroB1.Installer.Core.Settings;

/// <summary>Esconde senhas e chaves antes de qualquer texto ir para tela, resumo ou log.</summary>
public static partial class SecretMasker
{
    private const string Mask = "***";
    private static readonly string[] SecretWords = ["Password", "Secret", "Key", "Token"];

    public static string MaskConnectionString(string connectionString) => MaskText(connectionString);

    /// <summary>Decide pelo último segmento do caminho (ex.: "Email:Password").</summary>
    public static string? MaskValue(string keyPath, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (keyPath.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
        {
            return MaskConnectionString(value);
        }

        var last = keyPath[(keyPath.LastIndexOf(':') + 1)..];
        return SecretWords.Any(w => last.Contains(w, StringComparison.OrdinalIgnoreCase)) ? Mask : value;
    }

    /// <summary>Texto livre (saída de processo, mensagens): troca o valor de Password=/Pwd= por ***.</summary>
    public static string MaskText(string text) => PasswordPair().Replace(text, m => m.Groups[1].Value + Mask);

    [GeneratedRegex(@"((?:Password|Pwd)\s*=\s*)(?:""[^""]*""|'[^']*'|[^;""\s]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordPair();
}
