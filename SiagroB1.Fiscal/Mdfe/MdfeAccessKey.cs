using SiagroB1.Fiscal.Dfe;

namespace SiagroB1.Fiscal.Mdfe;

/// <summary>Chave de acesso do MDF-e (modelo 58) — fachada de <see cref="DfeAccessKey"/>.</summary>
public static class MdfeAccessKey
{
    public static string Build(string ufCode, DateTime issuedAt, string issuerCnpj, int series, int number, int emissionType, string randomCode) =>
        DfeAccessKey.Build(ufCode, issuedAt, issuerCnpj, "58", series, number, emissionType, randomCode);

    /// <summary>Chave de CT-e (modelo 57) com 44 posições e DV conferido — o que o MDF-e relaciona.</summary>
    public static bool IsValidCteKey(string key) => DfeAccessKey.IsValid(key, "57");
}
