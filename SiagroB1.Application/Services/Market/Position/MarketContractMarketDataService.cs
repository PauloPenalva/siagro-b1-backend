using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>Os cinco dados de mercado do contrato; o corpo substitui todos (nulo limpa).</summary>
public sealed record MarketContractMarketData(
    string? InstrumentCode, decimal? ExchangePrice, decimal? Premium, decimal? UsdPricePerKg, decimal? FxRate);

/// <summary>
/// Grava a referência de bolsa e o preço em US$ de um contrato APROVADO (o serviço de contratos só edita rascunho).
/// Mesma mecânica do <see cref="MarketContractFxLockService"/>: gravação direcionada às cinco colunas, uma entrada de log
/// por campo alterado na mesma gravação, <c>RowVersion</c> protegendo a edição simultânea e cache da exposição invalidado.
/// Não mexe em <c>MarketFxLocked</c>.
/// </summary>
public class MarketContractMarketDataService(
    AppDbContext context, MarketPermissionGuard guard,
    PurchaseContractsChangeLogService purchaseLog, SalesContractsChangeLogService salesLog,
    MarketExposureCache? exposureCache = null, ICompanyContext? companyContext = null)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private sealed record Change(string Field, string? Old, string? New);

    public async Task SetMarketDataAsync(MarketEfpContractSide side, Guid contractKey, MarketContractMarketData data,
        string userName, CancellationToken ct = default)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);

        var code = string.IsNullOrWhiteSpace(data.InstrumentCode) ? null : data.InstrumentCode.Trim();
        var exchange = Round(data.ExchangePrice, 6);
        var premium = Round(data.Premium, 6);
        var usd = Round(data.UsdPricePerKg, 8);
        var fx = Round(data.FxRate, 6);
        if (exchange <= 0) throw new BusinessException("A cotação da bolsa deve ser maior que zero.");
        if (usd <= 0) throw new BusinessException("O preço em US$ deve ser maior que zero.");
        if (fx <= 0) throw new BusinessException("A taxa de câmbio deve ser maior que zero.");

        try
        {
            if (side == MarketEfpContractSide.Purchase)
            {
                var contract = await context.PurchaseContracts.FirstOrDefaultAsync(c => c.Key == contractKey, ct)
                               ?? throw new KeyNotFoundException("Contrato de compra não encontrado.");
                EnsureApproved(contract.Status);
                await ValidateInstrumentAsync(code, contract.ItemCode, ct);
                EnsureFxRateKept(contract.MarketFxLocked, contract.MarketFxRate, fx);
                var changes = Changes(contract.MarketInstrumentCode, contract.MarketExchangePrice, contract.MarketPremium,
                    contract.MarketUsdPrice, contract.MarketFxRate, code, exchange, premium, usd, fx);
                if (changes.Count > 0)
                {
                    contract.MarketInstrumentCode = code;
                    contract.MarketExchangePrice = exchange;
                    contract.MarketPremium = premium;
                    contract.MarketUsdPrice = usd;
                    contract.MarketFxRate = fx;
                    foreach (var c in changes)
                        purchaseLog.Register(contractKey, c.Field, c.Old, c.New, userName);
                    await context.SaveChangesAsync(ct);
                }
            }
            else
            {
                var contract = await context.SalesContracts.FirstOrDefaultAsync(c => c.Key == contractKey, ct)
                               ?? throw new KeyNotFoundException("Contrato de venda não encontrado.");
                EnsureApproved(contract.Status);
                await ValidateInstrumentAsync(code, contract.ItemCode, ct);
                EnsureFxRateKept(contract.MarketFxLocked, contract.MarketFxRate, fx);
                var changes = Changes(contract.MarketInstrumentCode, contract.MarketExchangePrice, contract.MarketPremium,
                    contract.MarketUsdPrice, contract.MarketFxRate, code, exchange, premium, usd, fx);
                if (changes.Count > 0)
                {
                    contract.MarketInstrumentCode = code;
                    contract.MarketExchangePrice = exchange;
                    contract.MarketPremium = premium;
                    contract.MarketUsdPrice = usd;
                    contract.MarketFxRate = fx;
                    foreach (var c in changes)
                        salesLog.Register(contractKey, c.Field, c.Old, c.New, userName);
                    await context.SaveChangesAsync(ct);
                }
            }

            // A posição lê o cache de 30 s: a mudança tem de aparecer já.
            if (companyContext is not null) exposureCache?.Invalidate(companyContext.CompanyCode);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            throw new BusinessException(MarketContractFxLockService.ConcurrencyMessage);
        }
    }

    /// <summary>
    /// A referência tem de ser valorizável na posição: instrumento cadastrado, cotado por tonelada (não moeda) e da
    /// mesma commodity do produto do contrato (quando o produto tem commodity cadastrada).
    /// </summary>
    private async Task ValidateInstrumentAsync(string? code, string? itemCode, CancellationToken ct)
    {
        if (code is null) return;
        var instrument = await context.MarketInstruments.AsNoTracking()
                             .Where(i => i.Code == code).Select(i => new { i.Code, i.Commodity, i.QuoteUnit })
                             .FirstOrDefaultAsync(ct)
                         ?? throw new BusinessException("O instrumento de referência não está cadastrado.");
        if (instrument.QuoteUnit is not (MarketQuoteUnit.UsCentsPerBushel or MarketQuoteUnit.BrlPerBag or MarketQuoteUnit.UsdPerTon))
            throw new BusinessException("O instrumento de referência precisa ser de commodity (não de moeda).");

        var item = itemCode?.Trim();
        var product = await context.MarketCommodityItems.AsNoTracking()
            .Where(i => i.ItemCode == item).Select(i => new { i.CommodityCode, i.Commodity!.Name })
            .FirstOrDefaultAsync(ct);
        if (product is null || string.Equals(product.CommodityCode, instrument.Commodity?.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        var instrumentCommodity = instrument.Commodity?.Trim() ?? string.Empty;
        var instrumentName = await context.MarketCommodities.AsNoTracking()
            .Where(c => c.Code == instrumentCommodity).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? instrumentCommodity;
        throw new BusinessException(
            $"O instrumento {instrument.Code} é de {instrumentName} e o produto do contrato é de {product.Name}.");
    }

    /// <summary>Com o câmbio fechado, a taxa gravada no fechamento não pode ser apagada por aqui.</summary>
    private static void EnsureFxRateKept(bool locked, decimal? current, decimal? next)
    {
        if (locked && current is not null && next is null)
            throw new BusinessException("Reabra o câmbio antes de limpar a taxa do dólar.");
    }

    private static decimal? Round(decimal? value, int scale) =>
        value.HasValue ? Math.Round(value.Value, scale, MidpointRounding.AwayFromZero) : null;

    private static void EnsureApproved(ContractStatus? status)
    {
        if (status != ContractStatus.Approved)
            throw new BusinessException("Os dados de mercado só podem ser alterados em contrato aprovado; em rascunho, edite o contrato.");
    }

    /// <summary>Campos que realmente mudam, com o valor antigo e o novo já formatados para o log.</summary>
    private static List<Change> Changes(
        string? curCode, decimal? curExchange, decimal? curPremium, decimal? curUsd, decimal? curFx,
        string? code, decimal? exchange, decimal? premium, decimal? usd, decimal? fx)
    {
        var list = new List<Change>();
        if (curCode != code) list.Add(new Change("Instrumento de referência", curCode, code));
        Add(list, "Cotação da bolsa", curExchange, exchange, "0.######");
        Add(list, "Prêmio", curPremium, premium, "0.######");
        Add(list, "Preço em US$", curUsd, usd, "0.########");
        Add(list, "Dólar", curFx, fx, "0.######");
        return list;
    }

    private static void Add(List<Change> list, string field, decimal? current, decimal? next, string format)
    {
        if (current != next)
            list.Add(new Change(field, current?.ToString(format, PtBr), next?.ToString(format, PtBr)));
    }
}
