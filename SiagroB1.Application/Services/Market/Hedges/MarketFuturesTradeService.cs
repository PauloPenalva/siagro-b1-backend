using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Commons.Time;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>Corpo de criação/alteração de uma operação de bolsa (enums aceitos como texto, ex.: "buy").</summary>
public sealed record MarketFuturesTradeInput(
    DateTime TradedAt, int AccountId, string InstrumentCode,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MarketTradeSide Side,
    int Lots, decimal Price, decimal Fees,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CurrencyType FeesCurrency,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MarketFuturesTradeReason Reason,
    string? BrokerReference, Guid? PurchaseContractKey, Guid? SalesContractKey, string? Notes);

/// <summary>Dados de lote do instrumento para a tela mostrar toneladas (ou US$) enquanto o usuário digita.</summary>
public sealed record MarketFuturesLotInfo(
    string InstrumentCode, MarketQuoteUnit QuoteUnit, decimal ContractSize, decimal? TonsPerLot, decimal? UsdPerLot,
    CurrencyType Currency);

/// <summary>
/// Operações de bolsa (só registro) com casamento PEPS por conta × instrumento. Toda gravação refaz o casamento do
/// par (apaga e recria MARKET_FUTURES_MATCHES e OpenLots) no MESMO SaveChanges da operação, sob KeyedAsyncLock por
/// empresa + conta + instrumento — duas gravações no mesmo par nunca intercalam o recálculo.
/// </summary>
public class MarketFuturesTradeService(
    AppDbContext context,
    MarketPermissionGuard guard,
    TimeProvider time,
    KeyedAsyncLock locks,
    ICompanyContext companyContext)
{
    public const string ConcurrencyMessage = "A operação foi alterada por outro usuário. Atualize e tente de novo.";

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public Task<MarketFuturesTrade> CreateAsync(MarketFuturesTradeInput input, string userName, CancellationToken ct)
    {
        if (input.Reason == MarketFuturesTradeReason.Efp)
            throw new BusinessException("Operação de EFP só é gerada pela tela de EFP.");
        return CreateCoreAsync(input, null, userName, ct);
    }

    /// <summary>Uso interno do EFP: cria a operação marcada com o EFP (motivo Efp). Não exposto por REST.</summary>
    public Task<MarketFuturesTrade> CreateForEfpAsync(MarketFuturesTradeInput input, int efpId, string userName,
        CancellationToken ct) =>
        CreateCoreAsync(input with { Reason = MarketFuturesTradeReason.Efp }, efpId, userName, ct);

    public async Task<MarketFuturesTrade> UpdateAsync(int id, MarketFuturesTradeInput input, string userName,
        CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        if (input.Reason == MarketFuturesTradeReason.Efp)
            throw new BusinessException("Operação de EFP só é gerada pela tela de EFP.");
        // Conta ou instrumento inativos só barram quando MUDAM: operação antiga continua editável.
        var current = await context.MarketFuturesTrades.AsNoTracking().Where(t => t.Id == id)
            .Select(t => new { t.AccountId, t.InstrumentCode }).FirstOrDefaultAsync(ct);
        var validated = await ValidateAsync(input, ct, current?.AccountId, current?.InstrumentCode);

        return await WithTradeLockAsync(id, [Key(validated.AccountId, validated.InstrumentCode)], async trade =>
        {
            EnsureEditable(trade);
            var oldAccount = trade.AccountId;
            var oldInstrument = trade.InstrumentCode;
            Apply(trade, validated);
            trade.UpdatedBy = userName;
            trade.UpdatedAt = UtcNow;
            await RematchCoreAsync(trade.AccountId, trade.InstrumentCode, ct);
            if (oldAccount != trade.AccountId || !SameCode(oldInstrument, trade.InstrumentCode))
                await RematchCoreAsync(oldAccount, oldInstrument, ct);
            await SaveAsync(ct);
            return trade;
        }, ct);
    }

    public Task DeleteAsync(int id, string userName, CancellationToken ct) => DeleteCoreAsync(id, null, userName, ct);

    /// <summary>Uso interno do EFP: exclui a operação que pertence ao EFP informado. Não exposto por REST.</summary>
    public Task DeleteForEfpAsync(int id, int efpId, string userName, CancellationToken ct) =>
        DeleteCoreAsync(id, efpId, userName, ct);

    public async Task<MarketFuturesTrade> SetConfirmedAsync(int id, bool confirmed, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        return await WithTradeLockAsync(id, [], async trade =>
        {
            trade.Confirmed = confirmed;
            trade.ConfirmedBy = confirmed ? userName : null;
            trade.ConfirmedAt = confirmed ? UtcNow : null;
            await SaveAsync(ct);
            return trade;
        }, ct);
    }

    /// <summary>Confere de uma vez as operações ainda não conferidas da conta na data informada.</summary>
    public async Task<int> ConfirmDayAsync(int accountId, DateOnly date, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        var day = date.ToDateTime(TimeOnly.MinValue);
        var instruments = await context.MarketFuturesTrades.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.TradeDate == day && !t.Confirmed)
            .Select(t => t.InstrumentCode).Distinct().ToListAsync(ct);
        using var _ = await AcquireAsync(instruments.Select(i => Key(accountId, i)), ct);

        var trades = await context.MarketFuturesTrades
            .Where(t => t.AccountId == accountId && t.TradeDate == day && !t.Confirmed).ToListAsync(ct);
        var now = UtcNow;
        foreach (var trade in trades)
        {
            trade.Confirmed = true;
            trade.ConfirmedBy = userName;
            trade.ConfirmedAt = now;
        }

        await SaveAsync(ct);
        return trades.Count;
    }

    /// <summary>Refaz e grava o casamento PEPS de uma conta × instrumento (usado pelo EFP).</summary>
    public async Task RematchAsync(int accountId, string instrumentCode, CancellationToken ct)
    {
        using var _ = await AcquireAsync([Key(accountId, instrumentCode)], ct);
        await RematchCoreAsync(accountId, instrumentCode, ct);
        await SaveAsync(ct);
    }

    public async Task<MarketFuturesLotInfo> GetLotInfoAsync(string instrumentCode, CancellationToken ct)
    {
        var code = instrumentCode?.Trim() ?? string.Empty;
        var instrument = await context.MarketInstruments.AsNoTracking().FirstOrDefaultAsync(i => i.Code == code, ct)
                         ?? throw new KeyNotFoundException($"Instrumento {code} não encontrado.");
        return LotInfo(instrument);
    }

    public static MarketFuturesLotInfo LotInfo(MarketInstrument i) => new(
        i.Code, i.QuoteUnit, i.ContractSize, MarketUnits.TonsPerLot(i.QuoteUnit, i.ContractSize, i.TonFactor),
        MarketUnits.UsdPerLot(i.QuoteUnit, i.ContractSize), MarketUnits.QuoteCurrency(i.QuoteUnit));

    /// <summary>Toneladas com sinal da operação inteira (4 casas); nulo para dólar futuro.</summary>
    public static decimal? SignedTons(MarketFuturesTrade trade, MarketInstrument instrument) =>
        MarketUnits.TonsPerLot(instrument.QuoteUnit, instrument.ContractSize, instrument.TonFactor) is { } perLot
            ? Math.Round(perLot * trade.Lots * (trade.Side == MarketTradeSide.Buy ? 1 : -1), 4, MidpointRounding.AwayFromZero)
            : null;

    private async Task<MarketFuturesTrade> CreateCoreAsync(MarketFuturesTradeInput input, int? efpId, string userName,
        CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        var validated = await ValidateAsync(input, ct);
        using var _ = await AcquireAsync([Key(validated.AccountId, validated.InstrumentCode)], ct);

        var trade = new MarketFuturesTrade { InstrumentCode = validated.InstrumentCode };
        Apply(trade, validated);
        trade.EfpId = efpId;
        trade.CreatedBy = userName;
        trade.CreatedAt = UtcNow;
        context.MarketFuturesTrades.Add(trade);
        await RematchCoreAsync(trade.AccountId, trade.InstrumentCode, ct);
        await SaveAsync(ct);
        return trade;
    }

    private async Task DeleteCoreAsync(int id, int? efpId, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        await WithTradeLockAsync(id, [], async trade =>
        {
            if (efpId is null)
                EnsureEditable(trade);
            else
            {
                if (trade.EfpId != efpId)
                    throw new BusinessException($"A operação nº {id} não pertence a este EFP.");
                EnsureNotConfirmed(trade);
            }

            context.MarketFuturesTrades.Remove(trade);
            await RematchCoreAsync(trade.AccountId, trade.InstrumentCode, ct);
            await SaveAsync(ct);
            return trade;
        }, ct);
    }

    /// <summary>
    /// Trava o par atual da operação (mais os pares extras, ex.: destino de uma mudança de conta/instrumento) em ordem
    /// estável, recarrega do banco e confere que o par não mudou enquanto esperava; se mudou, tenta de novo.
    /// </summary>
    private async Task<MarketFuturesTrade> WithTradeLockAsync(int id, string[] extraKeys,
        Func<MarketFuturesTrade, Task<MarketFuturesTrade>> action, CancellationToken ct)
    {
        while (true)
        {
            var pair = await context.MarketFuturesTrades.AsNoTracking().Where(t => t.Id == id)
                           .Select(t => new { t.AccountId, t.InstrumentCode }).FirstOrDefaultAsync(ct)
                       ?? throw new KeyNotFoundException($"Operação {id} não encontrada.");
            using var _ = await AcquireAsync(extraKeys.Append(Key(pair.AccountId, pair.InstrumentCode)), ct);

            var trade = await context.MarketFuturesTrades.FirstOrDefaultAsync(t => t.Id == id, ct)
                        ?? throw new KeyNotFoundException($"Operação {id} não encontrada.");
            var entry = context.Entry(trade);
            await entry.ReloadAsync(ct);
            if (entry.State == EntityState.Detached)
                throw new KeyNotFoundException($"Operação {id} não encontrada.");
            if (trade.AccountId != pair.AccountId || !SameCode(trade.InstrumentCode, pair.InstrumentCode))
                continue;
            return await action(trade);
        }
    }

    /// <summary>
    /// Recalcula em memória (sem gravar) o casamento do par: considera o estado rastreado (operação nova, alterada ou
    /// excluída) e usa o tamanho de lote ATUAL do instrumento.
    /// </summary>
    private async Task RematchCoreAsync(int accountId, string instrumentCode, CancellationToken ct)
    {
        var instrument = await context.MarketInstruments.AsNoTracking()
                             .FirstOrDefaultAsync(i => i.Code == instrumentCode, ct)
                         ?? throw new BusinessException($"Instrumento {instrumentCode} não encontrado.");

        await context.MarketFuturesTrades
            .Where(t => t.AccountId == accountId && t.InstrumentCode == instrumentCode).LoadAsync(ct);
        var trades = context.MarketFuturesTrades.Local
            .Where(t => t.AccountId == accountId && SameCode(t.InstrumentCode, instrumentCode)
                        && context.Entry(t).State != EntityState.Deleted)
            .OrderBy(t => t.TradedAt).ThenBy(t => t.Id > 0 ? t.Id : int.MaxValue)
            .ToList();

        // Ids sintéticos na ordem PEPS: a operação nova (ainda sem Id) fica por último no mesmo instante.
        var byLeg = trades.Select((t, i) => (Leg: i + 1, Trade: t)).ToDictionary(x => x.Leg, x => x.Trade);
        var result = MarketFifoMatcher.Run(byLeg.Select(x =>
            new MarketFifoMatcher.Leg(x.Key, x.Value.TradedAt, x.Value.Side, x.Value.Lots, x.Value.Price)));

        var old = await context.MarketFuturesMatches
            .Where(m => m.AccountId == accountId && m.InstrumentCode == instrumentCode).ToListAsync(ct);
        context.MarketFuturesMatches.RemoveRange(old);

        var currency = MarketUnits.QuoteCurrency(instrument.QuoteUnit);
        foreach (var m in result.Matches)
        {
            var open = byLeg[m.OpenTradeId];
            var close = byLeg[m.CloseTradeId];
            context.MarketFuturesMatches.Add(new MarketFuturesMatch
            {
                AccountId = accountId,
                InstrumentCode = instrument.Code,
                OpenTrade = open,
                CloseTrade = close,
                Lots = m.Lots,
                OpenPrice = m.OpenPrice,
                ClosePrice = m.ClosePrice,
                RealizedPnl = MarketUnits.RealizedPnl(instrument.QuoteUnit, instrument.ContractSize, m.OpenSide,
                    m.OpenPrice, m.ClosePrice, m.Lots),
                Currency = currency,
                CloseDate = close.TradeDate,
            });
        }

        foreach (var (leg, trade) in byLeg)
            if (trade.OpenLots != result.OpenLots[leg])
                trade.OpenLots = result.OpenLots[leg];
    }

    private sealed record Validated(
        DateTime TradedAt, DateTime TradeDate, int AccountId, string InstrumentCode, MarketTradeSide Side, int Lots,
        decimal Price, decimal Fees, CurrencyType FeesCurrency, MarketFuturesTradeReason Reason, string? BrokerReference,
        Guid? PurchaseContractKey, Guid? SalesContractKey, string? ContractCode, string? Notes);

    private async Task<Validated> ValidateAsync(MarketFuturesTradeInput input, CancellationToken ct,
        int? currentAccountId = null, string? currentInstrumentCode = null)
    {
        if (input.TradedAt == default)
            throw new BusinessException("Informe a data e hora do negócio.");
        var tradedAt = input.TradedAt.Kind switch
        {
            DateTimeKind.Local => input.TradedAt.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(input.TradedAt, DateTimeKind.Utc),
            _ => input.TradedAt,
        };
        var tradeDate = TimeZoneInfo.ConvertTimeFromUtc(tradedAt, SaoPauloTime.Zone).Date;
        // Tolerância: até o fim do dia corrente em São Paulo.
        if (DateOnly.FromDateTime(tradeDate) > SaoPauloTime.Today(time))
            throw new BusinessException("A data do negócio não pode estar no futuro.");

        if (!Enum.IsDefined(input.Side))
            throw new BusinessException("Informe se a operação é de compra ou de venda.");
        if (!Enum.IsDefined(input.FeesCurrency))
            throw new BusinessException("Informe a moeda das taxas.");
        if (!Enum.IsDefined(input.Reason))
            throw new BusinessException("Informe o motivo da operação.");
        if (input.Lots <= 0)
            throw new BusinessException("A quantidade de lotes deve ser maior que zero.");
        var price = Math.Round(input.Price, 6, MidpointRounding.AwayFromZero);
        if (price <= 0)
            throw new BusinessException("O preço deve ser maior que zero.");
        var fees = Math.Round(input.Fees, 2, MidpointRounding.AwayFromZero);
        if (fees < 0)
            throw new BusinessException("As taxas não podem ser negativas.");

        var brokerReference = Trimmed(input.BrokerReference);
        if (brokerReference?.Length > 50)
            throw new BusinessException("A referência da corretora deve ter no máximo 50 caracteres.");
        var notes = Trimmed(input.Notes);
        if (notes?.Length > 500)
            throw new BusinessException("As observações devem ter no máximo 500 caracteres.");

        var account = await context.MarketCounterpartyAccounts.AsNoTracking().Include(a => a.Counterparty)
                          .FirstOrDefaultAsync(a => a.Id == input.AccountId, ct)
                      ?? throw new BusinessException("Conta não encontrada.");
        var sameAccount = currentAccountId == account.Id;
        if (!account.Active && !sameAccount)
            throw new BusinessException("A conta está inativa.");
        if (account.Counterparty?.Type != MarketCounterpartyType.Broker || (!account.Counterparty.Active && !sameAccount))
            throw new BusinessException("Operação de bolsa só vale para conta de uma corretora ativa.");

        var code = input.InstrumentCode?.Trim() ?? string.Empty;
        if (code.Length == 0)
            throw new BusinessException("Informe o instrumento.");
        var instrument = await context.MarketInstruments.AsNoTracking().FirstOrDefaultAsync(i => i.Code == code, ct)
                         ?? throw new BusinessException($"Instrumento {code} não encontrado.");
        if (!instrument.Active && !(currentInstrumentCode is not null && SameCode(currentInstrumentCode, instrument.Code)))
            throw new BusinessException($"O instrumento {instrument.Code} está inativo.");
        if (instrument.Type != MarketInstrumentType.Future)
            throw new BusinessException($"O instrumento {instrument.Code} não é um contrato futuro.");
        MarketUnits.EnsureTradable(instrument);

        if (input.PurchaseContractKey is not null && input.SalesContractKey is not null)
            throw new BusinessException("Vincule a operação a um contrato de compra OU de venda, não aos dois.");
        string? contractCode = null;
        if (input.PurchaseContractKey is { } purchaseKey)
            contractCode = await context.PurchaseContracts.AsNoTracking().Where(c => c.Key == purchaseKey)
                               .Select(c => c.Code).FirstOrDefaultAsync(ct)
                           ?? throw new BusinessException("Contrato de compra não encontrado.");
        if (input.SalesContractKey is { } salesKey)
            contractCode = await context.SalesContracts.AsNoTracking().Where(c => c.Key == salesKey)
                               .Select(c => c.Code).FirstOrDefaultAsync(ct)
                           ?? throw new BusinessException("Contrato de venda não encontrado.");

        return new Validated(tradedAt, tradeDate, account.Id, instrument.Code, input.Side, input.Lots, price, fees,
            input.FeesCurrency, input.Reason, brokerReference, input.PurchaseContractKey, input.SalesContractKey,
            contractCode, notes);
    }

    private static void Apply(MarketFuturesTrade trade, Validated v)
    {
        trade.TradedAt = v.TradedAt;
        trade.TradeDate = v.TradeDate;
        trade.AccountId = v.AccountId;
        trade.InstrumentCode = v.InstrumentCode;
        trade.Side = v.Side;
        trade.Lots = v.Lots;
        trade.Price = v.Price;
        trade.Fees = v.Fees;
        trade.FeesCurrency = v.FeesCurrency;
        trade.Reason = v.Reason;
        trade.BrokerReference = v.BrokerReference;
        trade.PurchaseContractKey = v.PurchaseContractKey;
        trade.SalesContractKey = v.SalesContractKey;
        trade.ContractCode = v.ContractCode;
        trade.Notes = v.Notes;
    }

    private static void EnsureEditable(MarketFuturesTrade trade)
    {
        if (trade.EfpId is not null)
            throw new BusinessException(
                $"A operação nº {trade.Id} foi gerada pelo EFP nº {trade.EfpId}; altere ou cancele pelo EFP.");
        EnsureNotConfirmed(trade);
    }

    private static void EnsureNotConfirmed(MarketFuturesTrade trade)
    {
        if (trade.Confirmed)
            throw new BusinessException(
                $"A operação nº {trade.Id} está conferida. Desmarque a conferência antes de alterar ou excluir.");
    }

    private string Key(int accountId, string instrumentCode) =>
        $"market-futures:{companyContext.CompanyCode}:{accountId}:{instrumentCode.Trim().ToUpperInvariant()}";

    /// <summary>Várias chaves em ordem estável (ordinal), para duas gravações cruzadas não se travarem.</summary>
    private async Task<IDisposable> AcquireAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        var held = new List<IDisposable>();
        try
        {
            foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                held.Add(await locks.AcquireAsync(key, ct));
        }
        catch
        {
            Release(held);
            throw;
        }

        return new Releaser(held);
    }

    private static void Release(List<IDisposable> held)
    {
        for (var i = held.Count - 1; i >= 0; i--)
            held[i].Dispose();
    }

    private sealed class Releaser(List<IDisposable> held) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Release(held);
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            throw new BusinessException(ConcurrencyMessage);
        }
        catch (DbUpdateException)
        {
            // Nada rastreado pela tentativa que falhou pode ir num SaveChanges futuro do mesmo escopo.
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static bool SameCode(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
