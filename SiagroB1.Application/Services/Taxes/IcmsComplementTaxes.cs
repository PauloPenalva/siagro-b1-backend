using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Tributos da linha do "Complemento de ICMS", comum à entrada (spec 2026-10-17 D4) e à saída (spec 2026-10-18 S5): o
/// operador informa base e valor do ICMS, o motor não calcula. Roda DEPOIS do motor (que, com valor 0, só resolve o CFOP e
/// zera o resto) e repõe o informado, com a alíquota derivada — só informativa — e o CST da linha original. Sem PIS/COFINS,
/// IBS/CBS com valor 0 (alíquotas e redução da natureza mantidas para o grupo do XML).
/// </summary>
internal static class IcmsComplementTaxes
{
    /// <summary>
    /// O CST da original quando é 00, 20 ou 90; CSOSN (Simples, 3 dígitos) → 900; qualquer outro, e o terceiro (sem
    /// original), → 90.
    /// </summary>
    public static string CstFor(string? originalCst)
    {
        var cst = originalCst?.Trim();
        if (cst is "00" or "20" or "90")
            return cst;

        return cst is { Length: 3 } ? "900" : "90";
    }

    /// <summary>
    /// Grava o ICMS informado e zera os demais tributos da linha (no IBS/CBS, só base e valores); a alíquota do ICMS é
    /// valor ÷ base × 100, com 4 casas.
    /// </summary>
    public static void Apply(INfeTaxedLine line, string cst, decimal typedBase, decimal typedValue)
    {
        line.CstIcms = cst;
        line.IcmsBase = typedBase;
        line.IcmsValue = typedValue;
        line.IcmsRate = typedBase == 0m ? 0m : Math.Round(typedValue / typedBase * 100m, 4);
        line.IcmsBaseReduction = 0m;
        line.IcmsDeferral = 0m;
        line.IcmsOperationValue = 0m;
        line.IcmsDeferredValue = 0m;

        line.PisBase = 0m;
        line.PisRate = 0m;
        line.PisValue = 0m;
        line.CofinsBase = 0m;
        line.CofinsRate = 0m;
        line.CofinsValue = 0m;

        // IBS/CBS sem base nem valor, mas com o CST, a classificação, as alíquotas e a redução que o motor resolveu pela
        // natureza: o cClassTrib com redução (ex.: 200036) exige o grupo gRed mesmo zerado — sem ele a SEFAZ-SP rejeita
        // com 1033 "Não informado o grupo de redução de alíquota Estadual" (homologação 17/10/2026).
        line.IbsCbsBase = 0m;
        line.CbsValue = 0m;
        line.IbsStateValue = 0m;
        line.IbsMunicipalValue = 0m;
    }
}
