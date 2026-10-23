namespace SiagroB1.Reports.Services.Ecf;

/// <summary>Resultado do cálculo de IRPJ ou CSLL do Lucro Real.</summary>
public sealed record IncomeTaxResult(
    decimal AdjustedProfit,
    decimal OffsetLimit,
    decimal OffsetApplied,
    bool OffsetExceedsLimit,
    decimal TaxableBase,
    decimal Tax,
    decimal Surtax,
    decimal Total);

/// <summary>Cálculo puro de IRPJ/CSLL (Lucro Real), usado pela ECF (M300/M350/N620/N630/N660/N670).</summary>
public static class IncomeTaxCalculator
{
    private const decimal IrpjRate = 0.15m;
    private const decimal SurtaxRate = 0.10m;
    private const decimal SurtaxMonthlyThreshold = 20_000m;
    private const decimal CsllRate = 0.09m;
    private const decimal OffsetLimitRate = 0.30m;

    public static IncomeTaxResult ComputeIrpj(decimal bookProfit, decimal additions, decimal exclusions,
        decimal lossOffsetRequested, int months)
    {
        if (months < 1 || months > 12)
            throw new ArgumentOutOfRangeException(nameof(months), months, "O período deve ter de 1 a 12 meses.");
        return Compute(bookProfit, additions, exclusions, lossOffsetRequested, IrpjRate, months);
    }

    public static IncomeTaxResult ComputeCsll(decimal bookProfit, decimal additions, decimal exclusions,
        decimal lossOffsetRequested)
        => Compute(bookProfit, additions, exclusions, lossOffsetRequested, CsllRate, null);

    private static IncomeTaxResult Compute(decimal bookProfit, decimal additions, decimal exclusions,
        decimal offsetRequested, decimal rate, int? surtaxMonths)
    {
        if (additions < 0) throw new ArgumentOutOfRangeException(nameof(additions), additions, "Não pode ser negativo.");
        if (exclusions < 0) throw new ArgumentOutOfRangeException(nameof(exclusions), exclusions, "Não pode ser negativo.");
        if (offsetRequested < 0) throw new ArgumentOutOfRangeException(nameof(offsetRequested), offsetRequested, "Não pode ser negativo.");

        var adjusted = bookProfit + additions - exclusions;
        var limit = adjusted > 0 ? Round(adjusted * OffsetLimitRate) : 0m;
        var applied = Math.Min(offsetRequested, limit);
        var taxableBase = Math.Max(0m, adjusted - applied);
        var tax = Round(taxableBase * rate);
        var surtax = surtaxMonths is int m
            ? Round(Math.Max(0m, taxableBase - SurtaxMonthlyThreshold * m) * SurtaxRate)
            : 0m;

        return new IncomeTaxResult(adjusted, limit, applied, offsetRequested > limit, taxableBase, tax, surtax, tax + surtax);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
