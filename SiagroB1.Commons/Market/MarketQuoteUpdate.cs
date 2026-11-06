namespace SiagroB1.Commons.Market;

/// <summary>
/// Atualização de cotação já interpretada, vinda do feed da CMA.
/// <see cref="QuotedAt"/> é nulo quando o feed não informou (ou enviou inválido) o horário.
/// </summary>
public sealed record MarketQuoteUpdate(
    string Symbol,
    decimal? Last,
    decimal? Bid,
    decimal? Ask,
    decimal? ChangePercent,
    decimal? High,
    decimal? Low,
    decimal? Open,
    decimal? PreviousClose,
    bool IsSettlement,
    DateTime? QuotedAt);
