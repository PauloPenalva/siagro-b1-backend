namespace SiagroB1.Fiscal.Taxes;

/// <summary>Funrural da linha: a base e as três parcelas, já arredondadas.</summary>
public sealed record FunruralResult(
    decimal Base, decimal InssRate, decimal RatRate, decimal SenarRate,
    decimal InssValue, decimal RatValue, decimal SenarValue)
{
    /// <summary>Linha sem Funrural: tudo zero, inclusive a base, para "sem Funrural" ser inequívoco.</summary>
    public static readonly FunruralResult None = new(0m, 0m, 0m, 0m, 0m, 0m, 0m);

    public decimal Total => InssValue + RatValue + SenarValue;
}

/// <summary>
/// Contribuição do produtor rural retida pelo comprador (spec 2026-10-09 §3.2): INSS, RAT e SENAR sobre o total
/// geral da linha. Alíquota vazia ou zero = parcela não retida; as três zeradas = linha sem Funrural. PF, PJ e
/// decisão judicial são naturezas diferentes — aqui só chegam as alíquotas.
/// </summary>
public static class FunruralCalculator
{
    public static FunruralResult Calculate(decimal amount, decimal? inssRate, decimal? ratRate, decimal? senarRate)
    {
        var inss = inssRate ?? 0m;
        var rat = ratRate ?? 0m;
        var senar = senarRate ?? 0m;

        if (inss == 0m && rat == 0m && senar == 0m)
            return FunruralResult.None;

        return new FunruralResult(amount, inss, rat, senar, Part(amount, inss), Part(amount, rat), Part(amount, senar));
    }

    private static decimal Part(decimal amount, decimal rate) =>
        decimal.Round(amount * rate / 100m, 2, MidpointRounding.AwayFromZero);
}
