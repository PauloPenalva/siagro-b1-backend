using SiagroB1.Fiscal.Dfe;

namespace SiagroB1.Fiscal.Cte;

/// <summary>Chave de acesso do CT-e (modelo 57) — fachada de <see cref="DfeAccessKey"/>.</summary>
public static class CteAccessKey
{
    public static string Build(string ufCode, DateTime issuedAt, string issuerCnpj, int series, int number, int emissionType, string randomCode) =>
        DfeAccessKey.Build(ufCode, issuedAt, issuerCnpj, "57", series, number, emissionType, randomCode);

    /// <summary>Chave de NF-e (modelo 55) com 44 posições e DV conferido.</summary>
    public static bool IsValidNfeKey(string key) => DfeAccessKey.IsValid(key, "55");

    internal static int CheckDigit(string body) => DfeAccessKey.CheckDigit(body);
}
