using System.Globalization;

namespace SiagroB1.Fiscal.Dfe;

/// <summary>
/// Chave de acesso de 44 posições comum a NF-e (55), CT-e (57) e MDF-e (58): cUF(2) AAMM(4)
/// CNPJ(14) mod(2) série(3) nº(9) tpEmis(1) código numérico(8) DV(1). O DV é módulo 11 com pesos
/// 2..9 da direita para a esquerda; cada caractere vale (ASCII − 48), o que cobre também o CNPJ
/// alfanumérico.
/// </summary>
public static class DfeAccessKey
{
    public static string Build(string ufCode, DateTime issuedAt, string issuerCnpj, string model, int series, int number, int emissionType, string randomCode)
    {
        var body = string.Concat(
            ufCode.PadLeft(2, '0'),
            issuedAt.ToString("yyMM", CultureInfo.InvariantCulture),
            issuerCnpj.PadLeft(14, '0'),
            model,
            series.ToString("000", CultureInfo.InvariantCulture),
            number.ToString("000000000", CultureInfo.InvariantCulture),
            emissionType.ToString(CultureInfo.InvariantCulture),
            randomCode.PadLeft(8, '0'));

        if (body.Length != 43)
            throw new ArgumentException($"Chave de acesso do modelo {model} com {body.Length + 1} posições; o esperado são 44.");

        return body + CheckDigit(body);
    }

    /// <summary>44 posições, DV conferido e, se informado, o modelo (posições 21–22).</summary>
    public static bool IsValid(string key, string? model = null) =>
        key.Length == 44
        && key.All(char.IsAsciiLetterOrDigit)
        && (model is null || key.Substring(20, 2) == model)
        && char.IsAsciiDigit(key[43])
        && CheckDigit(key[..43]) == key[43] - '0';

    internal static int CheckDigit(string body)
    {
        var sum = 0;
        var weight = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (char.ToUpperInvariant(body[i]) - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
