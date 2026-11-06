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

/// <summary>Corpo de criação/alteração de um hedge de moeda (enums aceitos como texto, ex.: "sell").</summary>
public sealed record MarketFxHedgeInput(
    DateTime TradedAt, int AccountId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MarketFxHedgeKind Kind,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MarketTradeSide Side,
    decimal AmountUsd, decimal ContractedRate, DateOnly SettlementDate, string? BankReference,
    Guid? PurchaseContractKey, Guid? SalesContractKey, string? Notes);

/// <summary>Taxa sugerida para a liquidação; <c>Source</c> é "dailyClose" ou "lastQuote".</summary>
public sealed record MarketFxSuggestedRate(decimal? Rate, string? Source, DateOnly? Date);

/// <summary>
/// Hedges de moeda (NDF/termo): só registro, sem lançamento financeiro. Toda gravação exige MARKET_MANAGE e roda sob
/// KeyedAsyncLock por empresa + hedge (a criação não precisa de trava).
/// </summary>
public class MarketFxHedgeService(
    AppDbContext context,
    MarketPermissionGuard guard,
    TimeProvider time,
    KeyedAsyncLock locks,
    ICompanyContext companyContext)
{
    public const string ConcurrencyMessage = "O hedge foi alterado por outro usuário. Atualize e tente de novo.";

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public async Task<MarketFxHedge> CreateAsync(MarketFxHedgeInput input, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        var v = await ValidateAsync(input, ct);
        var hedge = new MarketFxHedge { Status = MarketFxHedgeStatus.Open, CreatedBy = userName, CreatedAt = UtcNow };
        Apply(hedge, v);
        context.MarketFxHedges.Add(hedge);
        await SaveAsync(ct);
        return hedge;
    }

    public async Task<MarketFxHedge> UpdateAsync(int id, MarketFxHedgeInput input, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        var v = await ValidateAsync(input, ct);
        return await WithLockAsync(id, async hedge =>
        {
            EnsureEditable(hedge);
            Apply(hedge, v);
            hedge.UpdatedBy = userName;
            hedge.UpdatedAt = UtcNow;
            await SaveAsync(ct);
        }, ct);
    }

    public async Task DeleteAsync(int id, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        await WithLockAsync(id, async hedge =>
        {
            EnsureEditable(hedge);
            context.MarketFxHedges.Remove(hedge);
            await SaveAsync(ct);
        }, ct);
    }

    /// <summary>Liquida (inclusive antes do vencimento): grava taxa, resultado em R$ e quem/quando liquidou.</summary>
    public async Task<MarketFxHedge> SettleAsync(int id, decimal settlementRate, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        var rate = Math.Round(settlementRate, 6, MidpointRounding.AwayFromZero);
        return await WithLockAsync(id, async hedge =>
        {
            if (hedge.Status != MarketFxHedgeStatus.Open)
                throw new BusinessException($"Só é possível liquidar um hedge em aberto (nº {hedge.Id}).");
            if (rate <= 0)
                throw new BusinessException("A taxa de liquidação deve ser maior que zero.");
            hedge.Status = MarketFxHedgeStatus.Settled;
            hedge.SettlementRate = rate;
            hedge.SettledAt = UtcNow;
            hedge.SettledBy = userName;
            hedge.RealizedPnlBrl = MarketFxMath.RealizedPnlBrl(hedge.Side, hedge.ContractedRate, rate, hedge.AmountUsd);
            hedge.UpdatedBy = userName;
            hedge.UpdatedAt = UtcNow;
            await SaveAsync(ct);
        }, ct);
    }

    /// <summary>Volta a Aberto um hedge liquidado ou cancelado, limpando a liquidação.</summary>
    public Task<MarketFxHedge> ReopenAsync(int id, string userName, CancellationToken ct) =>
        ChangeStatusAsync(id, userName, MarketFxHedgeStatus.Open, ct);

    public Task<MarketFxHedge> CancelAsync(int id, string userName, CancellationToken ct) =>
        ChangeStatusAsync(id, userName, MarketFxHedgeStatus.Canceled, ct);

    public async Task<MarketFxHedge> SetConfirmedAsync(int id, bool confirmed, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        return await WithLockAsync(id, async hedge =>
        {
            hedge.Confirmed = confirmed;
            hedge.ConfirmedBy = confirmed ? userName : null;
            hedge.ConfirmedAt = confirmed ? UtcNow : null;
            await SaveAsync(ct);
        }, ct);
    }

    /// <summary>Fechamento diário do dólar spot na data de vencimento; senão a última cotação; senão nulos.</summary>
    public async Task<MarketFxSuggestedRate> SuggestSettlementRateAsync(int id, CancellationToken ct)
    {
        var settlementDate = await context.MarketFxHedges.AsNoTracking().Where(h => h.Id == id)
                                 .Select(h => (DateTime?)h.SettlementDate).FirstOrDefaultAsync(ct)
                             ?? throw new KeyNotFoundException($"Hedge {id} não encontrado.");
        var code = await context.MarketTradingSettings.AsNoTracking().Select(s => s.SpotFxInstrumentCode)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(code))
            return new MarketFxSuggestedRate(null, null, null);

        var day = settlementDate.Date;
        var close = await context.MarketDailyCloses.AsNoTracking()
            .Where(c => c.InstrumentCode == code && c.Date == day).Select(c => (decimal?)c.Close)
            .FirstOrDefaultAsync(ct);
        if (close is { } closeRate)
            return new MarketFxSuggestedRate(closeRate, "dailyClose", DateOnly.FromDateTime(day));

        var quote = await context.MarketQuotes.AsNoTracking()
            .Where(q => q.InstrumentCode == code && q.Last != null)
            .Select(q => new { q.Last, q.QuotedAt }).FirstOrDefaultAsync(ct);
        if (quote is null)
            return new MarketFxSuggestedRate(null, null, null);
        var quoted = DateTime.SpecifyKind(quote.QuotedAt, DateTimeKind.Utc);
        return new MarketFxSuggestedRate(quote.Last, "lastQuote",
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(quoted, SaoPauloTime.Zone)));
    }

    private async Task<MarketFxHedge> ChangeStatusAsync(int id, string userName, MarketFxHedgeStatus target,
        CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        return await WithLockAsync(id, async hedge =>
        {
            if (target == MarketFxHedgeStatus.Open)
            {
                if (hedge.Status == MarketFxHedgeStatus.Open)
                    throw new BusinessException($"O hedge nº {hedge.Id} já está em aberto.");
                hedge.SettlementRate = null;
                hedge.SettledAt = null;
                hedge.SettledBy = null;
                hedge.RealizedPnlBrl = null;
            }
            else if (hedge.Status != MarketFxHedgeStatus.Open)
                throw new BusinessException(
                    $"Só é possível cancelar um hedge em aberto (nº {hedge.Id}); reabra o hedge liquidado antes.");

            hedge.Status = target;
            hedge.UpdatedBy = userName;
            hedge.UpdatedAt = UtcNow;
            await SaveAsync(ct);
        }, ct);
    }

    private async Task<MarketFxHedge> WithLockAsync(int id, Func<MarketFxHedge, Task> action, CancellationToken ct)
    {
        using var _ = await locks.AcquireAsync($"market-fxhedge:{companyContext.CompanyCode}:{id}", ct);
        var hedge = await context.MarketFxHedges.FirstOrDefaultAsync(h => h.Id == id, ct)
                    ?? throw new KeyNotFoundException($"Hedge {id} não encontrado.");
        var entry = context.Entry(hedge);
        await entry.ReloadAsync(ct);
        if (entry.State == EntityState.Detached)
            throw new KeyNotFoundException($"Hedge {id} não encontrado.");
        await action(hedge);
        return hedge;
    }

    private sealed record Validated(
        DateTime TradedAt, DateTime TradeDate, int AccountId, MarketFxHedgeKind Kind, MarketTradeSide Side,
        decimal AmountUsd, decimal ContractedRate, DateTime SettlementDate, string? BankReference,
        Guid? PurchaseContractKey, Guid? SalesContractKey, string? ContractCode, string? Notes);

    private async Task<Validated> ValidateAsync(MarketFxHedgeInput input, CancellationToken ct)
    {
        if (input.TradedAt == default)
            throw new BusinessException("Informe a data e hora do negócio.");
        var tradedAt = input.TradedAt.Kind switch
        {
            DateTimeKind.Local => input.TradedAt.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(input.TradedAt, DateTimeKind.Utc),
            _ => input.TradedAt,
        };
        if (tradedAt > UtcNow.AddDays(1))
            throw new BusinessException("A data do negócio não pode estar no futuro.");
        var tradeDate = TimeZoneInfo.ConvertTimeFromUtc(tradedAt, SaoPauloTime.Zone).Date;

        if (!Enum.IsDefined(input.Kind))
            throw new BusinessException("Informe se o hedge é NDF ou termo.");
        if (!Enum.IsDefined(input.Side))
            throw new BusinessException("Informe se o hedge é de compra ou de venda de dólar.");
        var amount = Math.Round(input.AmountUsd, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            throw new BusinessException("O valor em US$ deve ser maior que zero.");
        if (amount > 9_999_999_999_999m)
            throw new BusinessException("O valor em US$ é grande demais.");
        var rate = Math.Round(input.ContractedRate, 6, MidpointRounding.AwayFromZero);
        if (rate <= 0)
            throw new BusinessException("A taxa contratada deve ser maior que zero.");
        if (rate > 999_999m)
            throw new BusinessException("A taxa contratada é alta demais.");
        var settlement = input.SettlementDate.ToDateTime(TimeOnly.MinValue);
        if (input.SettlementDate == default || settlement < tradeDate)
            throw new BusinessException("O vencimento não pode ser anterior à data do negócio.");

        var bankReference = Trimmed(input.BankReference);
        if (bankReference?.Length > 50)
            throw new BusinessException("A referência do banco deve ter no máximo 50 caracteres.");
        var notes = Trimmed(input.Notes);
        if (notes?.Length > 500)
            throw new BusinessException("As observações devem ter no máximo 500 caracteres.");

        var account = await context.MarketCounterpartyAccounts.AsNoTracking().Include(a => a.Counterparty)
                          .FirstOrDefaultAsync(a => a.Id == input.AccountId, ct)
                      ?? throw new BusinessException("Conta não encontrada.");
        if (!account.Active)
            throw new BusinessException("A conta está inativa.");
        if (account.Counterparty is not { Active: true, Type: MarketCounterpartyType.Bank })
            throw new BusinessException("Hedge de moeda só vale para conta de um banco ativo.");

        if (input.PurchaseContractKey is not null && input.SalesContractKey is not null)
            throw new BusinessException("Vincule o hedge a um contrato de compra OU de venda, não aos dois.");
        string? contractCode = null;
        if (input.PurchaseContractKey is { } purchaseKey)
            contractCode = await context.PurchaseContracts.AsNoTracking().Where(c => c.Key == purchaseKey)
                               .Select(c => c.Code).FirstOrDefaultAsync(ct)
                           ?? throw new BusinessException("Contrato de compra não encontrado.");
        if (input.SalesContractKey is { } salesKey)
            contractCode = await context.SalesContracts.AsNoTracking().Where(c => c.Key == salesKey)
                               .Select(c => c.Code).FirstOrDefaultAsync(ct)
                           ?? throw new BusinessException("Contrato de venda não encontrado.");

        return new Validated(tradedAt, tradeDate, account.Id, input.Kind, input.Side, amount, rate, settlement,
            bankReference, input.PurchaseContractKey, input.SalesContractKey, contractCode, notes);
    }

    private static void Apply(MarketFxHedge hedge, Validated v)
    {
        hedge.TradedAt = v.TradedAt;
        hedge.TradeDate = v.TradeDate;
        hedge.AccountId = v.AccountId;
        hedge.Kind = v.Kind;
        hedge.Side = v.Side;
        hedge.AmountUsd = v.AmountUsd;
        hedge.ContractedRate = v.ContractedRate;
        hedge.SettlementDate = v.SettlementDate;
        hedge.BankReference = v.BankReference;
        hedge.PurchaseContractKey = v.PurchaseContractKey;
        hedge.SalesContractKey = v.SalesContractKey;
        hedge.ContractCode = v.ContractCode;
        hedge.Notes = v.Notes;
    }

    private static void EnsureEditable(MarketFxHedge hedge)
    {
        if (hedge.Status != MarketFxHedgeStatus.Open)
            throw new BusinessException($"Só é possível alterar ou excluir um hedge em aberto (nº {hedge.Id}).");
        if (hedge.Confirmed)
            throw new BusinessException(
                $"O hedge nº {hedge.Id} está conferido. Desmarque a conferência antes de alterar ou excluir.");
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
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
