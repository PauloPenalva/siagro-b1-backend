using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>
/// Casamento PEPS (primeiro a entrar, primeiro a sair) das operações de uma conta × instrumento.
/// Pura e determinística: cada operação consome primeiro os lotes abertos mais antigos do lado
/// oposto; o que sobra fica aberto. Operações do mesmo lado nunca se casam.
/// </summary>
public static class MarketFifoMatcher
{
    public sealed record Leg(int Id, DateTime TradedAt, MarketTradeSide Side, int Lots, decimal Price);

    /// <summary>Casamento: <c>OpenTradeId</c> é a perna mais antiga, <c>CloseTradeId</c> a mais nova.</summary>
    public sealed record Match(int OpenTradeId, int CloseTradeId, int Lots, decimal OpenPrice, decimal ClosePrice,
        MarketTradeSide OpenSide);

    public sealed record Result(IReadOnlyList<Match> Matches, IReadOnlyDictionary<int, int> OpenLots);

    private sealed class Open(Leg leg)
    {
        public Leg Leg { get; } = leg;
        public int Remaining { get; set; } = leg.Lots;
    }

    public static Result Run(IEnumerable<Leg> legs)
    {
        var ordered = legs.OrderBy(l => l.TradedAt).ThenBy(l => l.Id).ToList();
        var openLots = ordered.ToDictionary(l => l.Id, l => Math.Max(l.Lots, 0));
        var matches = new List<Match>();
        var queues = new Dictionary<MarketTradeSide, Queue<Open>>
        {
            [MarketTradeSide.Buy] = new(),
            [MarketTradeSide.Sell] = new()
        };

        foreach (var leg in ordered)
        {
            if (leg.Lots <= 0) continue;

            var current = new Open(leg);
            var opposite = queues[leg.Side == MarketTradeSide.Buy ? MarketTradeSide.Sell : MarketTradeSide.Buy];

            while (current.Remaining > 0 && opposite.Count > 0)
            {
                var older = opposite.Peek();
                var lots = Math.Min(current.Remaining, older.Remaining);
                matches.Add(new Match(older.Leg.Id, leg.Id, lots, older.Leg.Price, leg.Price, older.Leg.Side));
                older.Remaining -= lots;
                current.Remaining -= lots;
                openLots[older.Leg.Id] = older.Remaining;
                if (older.Remaining == 0) opposite.Dequeue();
            }

            openLots[leg.Id] = current.Remaining;
            if (current.Remaining > 0) queues[leg.Side].Enqueue(current);
        }

        return new Result(matches, openLots);
    }
}
