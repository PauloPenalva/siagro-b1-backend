namespace SiagroB1.Application.Services.Market.Position;

/// <summary>
/// Regras puras da posição do Mercado: mês de bolsa, volume firme a entregar, cobertura de hedge
/// e estouro de limite. Sem dependências e sem acesso a banco.
/// </summary>
public static class MarketPositionMath
{
    public readonly record struct FuturesMonth(int Year, int Month);

    /// <summary>Regra do mapa da commodity: mês de entrega → mês de bolsa (com deslocamento de ano).</summary>
    public sealed record MonthRule(int DeliveryMonth, int FuturesMonth, int YearOffset);

    public enum LimitState { Ok, LongExceeded, ShortExceeded }

    /// <summary>Mês de bolsa da entrega; nulo quando não há regra para o mês de entrega.</summary>
    public static FuturesMonth? MapDelivery(DateOnly deliveryDate, IReadOnlyCollection<MonthRule> rules)
    {
        var rule = rules.FirstOrDefault(r => r.DeliveryMonth == deliveryDate.Month);
        return rule is null ? null : new FuturesMonth(deliveryDate.Year + rule.YearOffset, rule.FuturesMonth);
    }

    /// <summary>
    /// Volume (kg) com preço firme ainda não entregue. O entregue consome primeiro o volume com preço;
    /// nunca é negativo nem maior que o saldo físico.
    /// </summary>
    public static decimal FirmOpenKg(decimal totalVolume, decimal allocated, decimal washedOut,
        decimal washedOutUnfixed, decimal confirmedFixed)
    {
        var balance = totalVolume - allocated - washedOut;
        var washedFixed = washedOut - washedOutUnfixed;
        return Math.Max(0m, Math.Min(balance, confirmedFixed - washedFixed - allocated));
    }

    /// <summary>Volume do hedge no sentido contrário ao físico líquido, limitado ao próprio físico.</summary>
    public static decimal Covered(decimal physicalNet, decimal hedge)
    {
        if (physicalNet == 0m || Math.Sign(physicalNet) == Math.Sign(hedge)) return 0m;
        return Math.Min(Math.Abs(physicalNet), Math.Abs(hedge));
    }

    /// <summary>Percentual coberto (2 casas); nulo quando o físico líquido é zero.</summary>
    public static decimal? HedgePercent(decimal physicalNet, decimal hedge)
        => physicalNet == 0m
            ? null
            : Math.Round(Covered(physicalNet, hedge) / Math.Abs(physicalNet) * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Estourado quando líquido &gt; MaxLong ou líquido &lt; −MaxShort.</summary>
    public static LimitState Evaluate(decimal net, decimal? maxLong, decimal? maxShort)
    {
        if (maxLong.HasValue && net > maxLong.Value) return LimitState.LongExceeded;
        if (maxShort.HasValue && net < -maxShort.Value) return LimitState.ShortExceeded;
        return LimitState.Ok;
    }

    /// <summary>
    /// Mapa padrão: cada mês de entrega (1–12) vai para o primeiro mês de bolsa &gt;= ele; depois do
    /// último mês de bolsa vai para o primeiro do ano seguinte (YearOffset 1).
    /// </summary>
    public static IReadOnlyList<MonthRule> DefaultRules(IReadOnlyCollection<int> futuresMonths)
    {
        if (futuresMonths.Count == 0) return Array.Empty<MonthRule>();
        var sorted = futuresMonths.Distinct().Order().ToList();
        var rules = new List<MonthRule>(12);
        for (var month = 1; month <= 12; month++)
        {
            var next = sorted.Where(m => m >= month).Select(m => (int?)m).FirstOrDefault();
            rules.Add(next.HasValue
                ? new MonthRule(month, next.Value, 0)
                : new MonthRule(month, sorted[0], 1));
        }
        return rules;
    }
}
