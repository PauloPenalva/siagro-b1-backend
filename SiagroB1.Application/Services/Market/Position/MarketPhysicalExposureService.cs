using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

public sealed record MarketPhysicalRow(MarketEfpContractSide Side, Guid ContractKey, string ContractCode, string CardName, string ItemCode,
    string CommodityCode, int FuturesYear, int FuturesMonth, DateOnly DeliveryEndDate, decimal FirmOpenKg, decimal PricePerKg, CurrencyType Currency,
    string? MarketInstrumentCode, decimal? MarketExchangePrice, decimal? MarketPremium,
    bool UsdLinked, bool FxLocked, decimal? UsdPricePerKg, DateOnly CashFlowDate);

/// <summary>Contrato que teria volume firme a entregar mas ficou fora da posição. <c>Reason</c> em pt-BR.</summary>
public sealed record MarketIgnoredContract(MarketEfpContractSide Side, Guid ContractKey, string ContractCode, string ItemCode, string Reason);

public sealed record MarketPhysicalExposure(DateTime ReadAt, IReadOnlyList<MarketPhysicalRow> Rows, IReadOnlyList<MarketIgnoredContract> Ignored);

/// <summary>
/// Exposição física: volume com preço firme ainda não entregue dos contratos aprovados em KG, por commodity e
/// mês de bolsa. Lê só escalares em lote (6 consultas traduzíveis, sem N+1) e faz a conta em memória com
/// <see cref="MarketPositionMath"/>. Volumes = colunas do "Saldo físico" da tela de contratos.
/// </summary>
public class MarketPhysicalExposureService(
    AppDbContext context, MarketExposureCache cache, ICompanyContext companyContext, TimeProvider time)
{
    public const string ReasonNoCommodity = "Produto sem commodity configurada";
    public const string ReasonNotKg = "Contrato em unidade diferente de KG";
    public const string ReasonNoMonthRule = "Mês de entrega sem vencimento de bolsa configurado";
    public const string ReasonNoDeliveryEnd = "Contrato sem data final de entrega";

    private sealed record ContractData(MarketEfpContractSide Side, Guid Key, string? Code, string? CardName, string ItemCode,
        string UnitOfMeasureCode, ContractType Type, decimal TotalVolume, decimal AllocatedVolume, decimal WashedOutVolume,
        decimal WashedOutUnfixedVolume, decimal ContractPrice, CurrencyType? Currency, DateTime DeliveryEndDate,
        DateTime? CashFlowDate, string? MarketInstrumentCode, decimal? MarketExchangePrice, decimal? MarketPremium,
        decimal? MarketUsdPrice, bool MarketFxLocked);

    private sealed record FixationTotals(Guid Key, decimal Volume, decimal Amount);

    public async Task<MarketPhysicalExposure> GetAsync(CancellationToken ct)
    {
        var company = companyContext.CompanyCode;
        if (cache.TryGet<MarketPhysicalExposure>(company, out var cached))
            return cached!;

        var fresh = await ReadAsync(ct);
        cache.Set(company, fresh);
        return fresh;
    }

    public async Task<MarketPhysicalExposure> ReadAsync(CancellationToken ct)
    {
        var purchases = await context.PurchaseContracts.AsNoTracking()
            .Where(c => c.Status == ContractStatus.Approved)
            .Select(c => new ContractData(MarketEfpContractSide.Purchase, c.Key, c.Code, c.CardName, c.ItemCode,
                c.UnitOfMeasureCode, c.Type, c.TotalVolume, c.AllocatedVolume, c.WashedOutVolume, c.WashedOutUnfixedVolume,
                c.StandardPrice, c.StandardCurrency, c.DeliveryEndDate, c.StandardCashFlowDate, c.MarketInstrumentCode,
                c.MarketExchangePrice, c.MarketPremium, c.MarketUsdPrice, c.MarketFxLocked))
            .ToListAsync(ct);

        var sales = await context.SalesContracts.AsNoTracking()
            .Where(c => c.Status == ContractStatus.Approved)
            .Select(c => new ContractData(MarketEfpContractSide.Sales, c.Key, c.Code, c.CardName, c.ItemCode,
                c.UnitOfMeasureCode, c.Type, c.TotalVolume, c.AllocatedVolume, c.WashedOutVolume, c.WashedOutUnfixedVolume,
                c.Price, c.StandardCurrency, c.DeliveryEndDate, c.StandardCashFlowDate, c.MarketInstrumentCode,
                c.MarketExchangePrice, c.MarketPremium, c.MarketUsdPrice, c.MarketFxLocked))
            .ToListAsync(ct);

        var purchaseFixations = await context.PurchaseContractsPriceFixations.AsNoTracking()
            .Where(f => f.Status == PriceFixationStatus.Confirmed && f.PurchaseContractKey != null
                        && f.PurchaseContract!.Status == ContractStatus.Approved)
            .GroupBy(f => f.PurchaseContractKey!.Value)
            .Select(g => new FixationTotals(g.Key, g.Sum(f => f.FixationVolume), g.Sum(f => f.FixationVolume * f.FixationPrice)))
            .ToListAsync(ct);

        var salesFixations = await context.SalesContractsPriceFixations.AsNoTracking()
            .Where(f => f.Status == PriceFixationStatus.Confirmed && f.SalesContractKey != null
                        && f.SalesContract!.Status == ContractStatus.Approved)
            .GroupBy(f => f.SalesContractKey!.Value)
            .Select(g => new FixationTotals(g.Key, g.Sum(f => f.FixationVolume), g.Sum(f => f.FixationVolume * f.FixationPrice)))
            .ToListAsync(ct);

        var items = await context.MarketCommodityItems.AsNoTracking()
            .Where(i => i.Commodity!.Active)
            .Select(i => new { i.ItemCode, i.CommodityCode })
            .ToListAsync(ct);

        var months = await context.MarketCommodityMonths.AsNoTracking()
            .Where(m => m.Commodity!.Active)
            .Select(m => new { m.CommodityCode, m.DeliveryMonth, m.FuturesMonth, m.YearOffset })
            .ToListAsync(ct);

        var commodityByItem = items
            .GroupBy(i => i.ItemCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CommodityCode, StringComparer.OrdinalIgnoreCase);
        var rulesByCommodity = months
            .GroupBy(m => m.CommodityCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyCollection<MarketPositionMath.MonthRule>)g
                    .Select(m => new MarketPositionMath.MonthRule(m.DeliveryMonth, m.FuturesMonth, m.YearOffset)).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var fixationsBySide = new Dictionary<MarketEfpContractSide, Dictionary<Guid, FixationTotals>>
        {
            [MarketEfpContractSide.Purchase] = purchaseFixations.ToDictionary(f => f.Key),
            [MarketEfpContractSide.Sales] = salesFixations.ToDictionary(f => f.Key),
        };

        var rows = new List<MarketPhysicalRow>();
        var ignored = new List<MarketIgnoredContract>();

        foreach (var c in purchases.Concat(sales))
        {
            fixationsBySide[c.Side].TryGetValue(c.Key, out var fix);
            var (confirmed, price) = FirmVolumeAndPrice(c, fix);

            var firmOpen = MarketPositionMath.FirmOpenKg(c.TotalVolume, c.AllocatedVolume, c.WashedOutVolume,
                c.WashedOutUnfixedVolume, confirmed);
            if (firmOpen <= 0m) continue;

            if (!string.Equals(c.UnitOfMeasureCode?.Trim(), "KG", StringComparison.OrdinalIgnoreCase))
            {
                ignored.Add(Ignored(c, ReasonNotKg));
                continue;
            }

            if (c.DeliveryEndDate == default)
            {
                ignored.Add(Ignored(c, ReasonNoDeliveryEnd));
                continue;
            }

            if (!commodityByItem.TryGetValue(c.ItemCode.Trim(), out var commodity))
            {
                ignored.Add(Ignored(c, ReasonNoCommodity));
                continue;
            }

            var deliveryEnd = DateOnly.FromDateTime(c.DeliveryEndDate);
            var futures = rulesByCommodity.TryGetValue(commodity, out var rules)
                ? MarketPositionMath.MapDelivery(deliveryEnd, rules)
                : null;
            if (futures is null)
            {
                ignored.Add(Ignored(c, ReasonNoMonthRule));
                continue;
            }

            var currency = c.Currency ?? CurrencyType.Brl;
            var usdLinked = currency == CurrencyType.Usd || c.MarketUsdPrice.HasValue;
            var usdPrice = c.MarketUsdPrice ?? (currency == CurrencyType.Usd ? price : null);

            rows.Add(new MarketPhysicalRow(c.Side, c.Key, c.Code ?? "", c.CardName ?? "", c.ItemCode, commodity,
                futures.Value.Year, futures.Value.Month, deliveryEnd, firmOpen, price, currency,
                c.MarketInstrumentCode, c.MarketExchangePrice, c.MarketPremium, usdLinked, c.MarketFxLocked, usdPrice,
                c.CashFlowDate.HasValue ? DateOnly.FromDateTime(c.CashFlowDate.Value) : deliveryEnd));
        }

        return new MarketPhysicalExposure(time.GetUtcNow().UtcDateTime, rows, ignored);
    }

    private static MarketIgnoredContract Ignored(ContractData c, string reason) =>
        new(c.Side, c.Key, c.Code ?? "", c.ItemCode, reason);

    /// <summary>
    /// Volume com preço firme (Σ fixações confirmadas) e preço médio ponderado. Contrato de preço FIXO sem
    /// nenhuma fixação confirmada (legado anterior à fixação automática) conta inteiro ao preço do contrato.
    /// </summary>
    private static (decimal Confirmed, decimal Price) FirmVolumeAndPrice(ContractData c, FixationTotals? fix)
    {
        if (fix is not null && fix.Volume > 0m)
            return (fix.Volume, decimal.Round(fix.Amount / fix.Volume, 8, MidpointRounding.AwayFromZero));
        if (c.Type == ContractType.Fixed && fix is null)
            return (c.TotalVolume, c.ContractPrice);
        return (0m, 0m);
    }
}
