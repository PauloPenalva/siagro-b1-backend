using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Taxes;

/// <summary>
/// Códigos fiscais que este sub-projeto sabe calcular. A tela da natureza só aceita estes, e o
/// <see cref="TaxCalculator"/> só trata estes — a lista é a mesma nos dois para nenhum código
/// passar pelo cadastro e cair num ramo que o cálculo não conhece.
/// ST (CST 10/30/60/70, CSOSN 201/202/203/500) e o CSOSN 101 ficam fora por decisão do spec.
/// </summary>
public static class FiscalCodes
{
    /// <summary>CST de ICMS que destacam imposto (base × alíquota).</summary>
    public static readonly IReadOnlySet<string> IcmsCstTaxed = new HashSet<string> { "00", "20", "51", "90" };

    /// <summary>CST de ICMS sem imposto: isenta (40), não tributada (41), suspensão (50).</summary>
    public static readonly IReadOnlySet<string> IcmsCstNoTax = new HashSet<string> { "40", "41", "50" };

    public static readonly IReadOnlySet<string> IcmsCst = IcmsCstTaxed.Concat(IcmsCstNoTax).ToHashSet();

    public static readonly IReadOnlySet<string> IcmsCsosnTaxed = new HashSet<string> { "900" };

    public static readonly IReadOnlySet<string> IcmsCsosnNoTax = new HashSet<string> { "102", "103", "300", "400" };

    public static readonly IReadOnlySet<string> IcmsCsosn = IcmsCsosnTaxed.Concat(IcmsCsosnNoTax).ToHashSet();

    /// <summary>CST de ICMS que aceitam redução de base.</summary>
    public static readonly IReadOnlySet<string> IcmsCodesWithReduction = new HashSet<string> { "20", "51", "90", "900" };

    /// <summary>
    /// CST 03 (alíquota por unidade de medida, R$/unidade) fica de fora: o cálculo deste
    /// sub-projeto é percentual sobre a base, e o 03 sairia errado numa linha travada.
    /// </summary>
    public static readonly IReadOnlySet<string> PisCofinsCstOutgoing =
        new HashSet<string> { "01", "02", "04", "05", "06", "07", "08", "09", "49" };

    /// <summary>CST de PIS/COFINS que tributam por alíquota: sem ela a linha sairia zerada em silêncio.</summary>
    public static readonly IReadOnlySet<string> PisCofinsCstRequiringRate = new HashSet<string> { "01", "02" };

    public static readonly IReadOnlySet<string> PisCofinsCstIncoming = new HashSet<string>
    {
        "50", "51", "52", "53", "54", "55", "56",
        "60", "61", "62", "63", "64", "65", "66", "67",
        "70", "71", "72", "73", "74", "75", "98", "99",
    };

    /// <summary>Monofásico, ST, alíquota zero, isento, sem incidência, suspensão: base e valor zerados.</summary>
    public static readonly IReadOnlySet<string> PisCofinsCstNoTax =
        new HashSet<string> { "04", "05", "06", "07", "08", "09" };

    public static readonly IReadOnlySet<string> IbsCbsCst = new HashSet<string> { "000", "200", "400", "410" };

    /// <summary>Isenção (400) e imunidade/não incidência (410): sem base nem valor.</summary>
    public static readonly IReadOnlySet<string> IbsCbsCstNoTax = new HashSet<string> { "400", "410" };

    /// <summary>Origem da mercadoria com alíquota interestadual de 4% (Res. Senado 13/2012).</summary>
    public static readonly IReadOnlySet<byte> ImportedGoodsOrigins = new HashSet<byte> { 1, 2, 3, 8 };

    /// <summary>Select vazio da tela chega como "": no banco o "não informado" é nulo.</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Regimes que usam CSOSN em vez de CST: Simples Nacional (1) e MEI (4).</summary>
    public static bool UsesCsosn(TaxRegime regime) =>
        regime is TaxRegime.SimplesNacional or TaxRegime.Mei;
}
