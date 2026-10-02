namespace SiagroB1.Fiscal.Taxes;

/// <summary>
/// Alíquota interestadual do ICMS pela Resolução do Senado — lei fixa, não motor de regra:
/// importado (origem 1, 2, 3, 8) = 4% (Res. 13/2012); de S/SE exceto ES para N/NE/CO/ES = 7%
/// (Res. 22/89); demais = 12%. Origens 6 e 7 (importado sem similar, lista CAMEX) ficam fora
/// dos 4% por definição da própria resolução.
/// </summary>
public static class InterstateIcmsRate
{
    private static readonly HashSet<string> SouthSoutheastExceptEs =
        new(StringComparer.OrdinalIgnoreCase) { "PR", "SC", "RS", "SP", "RJ", "MG" };

    private static readonly HashSet<string> NorthNortheastMidwestAndEs = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AM", "AP", "PA", "RO", "RR", "TO",
        "AL", "BA", "CE", "MA", "PB", "PE", "PI", "RN", "SE",
        "DF", "GO", "MS", "MT", "ES",
    };

    public static decimal Resolve(string originState, string destinationState, byte goodsOrigin)
    {
        if (FiscalCodes.ImportedGoodsOrigins.Contains(goodsOrigin))
            return 4m;

        if (SouthSoutheastExceptEs.Contains(originState) && NorthNortheastMidwestAndEs.Contains(destinationState))
            return 7m;

        return 12m;
    }
}
