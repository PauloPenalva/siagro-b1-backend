using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Commons.Time;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>Corpo do EFP (preview e criação; enums aceitos como texto, ex.: "sell", "purchase").</summary>
public sealed record MarketEfpInput(
    DateOnly Date, int AccountId, string InstrumentCode,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MarketTradeSide Side,
    int Lots, decimal Price, decimal Premium, decimal? FxRate,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MarketEfpContractSide ContractSide,
    Guid ContractKey, decimal VolumeKg, DateOnly? FinancialDueDate, string? Notes);

public sealed record MarketEfpPreview(
    decimal FixationPricePerKg, decimal FixationPricePerBag, CurrencyType Currency, decimal? TonsFutures,
    decimal TonsPhysical);

/// <summary>Contrato a fixar oferecido no value help do EFP.</summary>
public sealed record MarketEfpContractOption(
    Guid Key, string? Code, string? CardName, string? ItemName, CurrencyType Currency, decimal AvailableToPriceKg,
    DateTime? StandardCashFlowDate);

/// <summary>Os serviços EXISTENTES de fixação de preço (compra e venda) que o EFP encadeia.</summary>
public sealed record MarketEfpFixations(
    PurchaseContractsPriceFixationCreateService PurchaseCreate,
    PurchaseContractsPriceFixationsApprovalService PurchaseApproval,
    PurchaseContractsPriceFixationsCancelService PurchaseReverse,
    PurchaseContractsPriceFixationDeleteService PurchaseDelete,
    SalesContractsPriceFixationCreateService SalesCreate,
    SalesContractsPriceFixationsApprovalService SalesApproval,
    SalesContractsPriceFixationsCancelService SalesReverse,
    SalesContractsPriceFixationDeleteService SalesDelete);

/// <summary>
/// EFP: operação de bolsa + fixação de preço num contrato a fixar, compondo serviços que têm transação própria
/// (por isso SEM transação ambiente). Idempotência no molde da boleta: a primeira gravação cria o EFP
/// <c>Processing</c> com tudo o que é preciso para terminar depois, inclusive a chave PRÉ-ATRIBUÍDA da fixação; cada
/// passo olha o estado ATUAL do banco antes de agir (operação localizada por <c>EfpId</c>, fixação pela chave), sob
/// <see cref="KeyedAsyncLock"/> por empresa + EFP. Depois da primeira gravação o token da requisição não é mais
/// honrado. Falha no meio deixa o EFP <c>Processing</c>; <see cref="ResumeAsync"/> continua sem duplicar nada.
/// </summary>
public class MarketEfpService(
    AppDbContext context,
    MarketPermissionGuard guard,
    TimeProvider time,
    KeyedAsyncLock locks,
    ICompanyContext companyContext,
    MarketFuturesTradeService trades,
    MarketEfpFixations fixations)
{
    public const decimal KgPerBag = 60m;

    private sealed record ContractInfo(
        Guid Key, string? Code, ContractStatus? Status, ContractType Type, string UnitOfMeasureCode,
        decimal TotalVolume, decimal WashedOutUnfixedVolume, CurrencyType? Currency, DateTime? CashFlowDate);

    private sealed record Validated(
        MarketEfpInput Input, string InstrumentCode, ContractInfo Contract, CurrencyType Currency, decimal? FxRate,
        decimal VolumeKg, decimal FixationPrice, DateTime DueDate, decimal? TonsFutures, string? Notes);

    public async Task<MarketEfpPreview> PreviewAsync(MarketEfpInput input, CancellationToken ct)
    {
        var v = await ValidateAsync(input, ct);
        return new MarketEfpPreview(v.FixationPrice,
            Math.Round(v.FixationPrice * KgPerBag, 2, MidpointRounding.AwayFromZero), v.Currency, v.TonsFutures,
            v.VolumeKg / 1000m);
    }

    public async Task<MarketEfp> CreateAsync(MarketEfpInput input, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        var v = await ValidateAsync(input, ct);

        var efp = new MarketEfp
        {
            Date = input.Date.ToDateTime(TimeOnly.MinValue),
            AccountId = input.AccountId,
            InstrumentCode = v.InstrumentCode,
            Side = input.Side,
            Lots = input.Lots,
            Price = input.Price,
            Premium = input.Premium,
            FxRate = v.FxRate,
            ContractSide = input.ContractSide,
            ContractKey = v.Contract.Key,
            ContractCode = v.Contract.Code ?? string.Empty,
            VolumeKg = v.VolumeKg,
            FixationPrice = v.FixationPrice,
            FinancialDueDate = v.DueDate,
            FixationKey = Guid.NewGuid(),
            Status = MarketEfpStatus.Processing,
            Notes = v.Notes,
            CreatedBy = userName,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        context.MarketEfps.Add(efp);
        await context.SaveChangesAsync(ct);

        // Daqui em diante o EFP existe: o token da requisição não é mais honrado (abandonar no meio só deixaria
        // trabalho para a retomada).
        return await RunLockedAsync(efp.Id, userName, CancellationToken.None);
    }

    public async Task<MarketEfp> ResumeAsync(int id, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        return await RunLockedAsync(id, userName, ct);
    }

    public async Task<MarketEfp> CancelAsync(int id, string userName, CancellationToken ct)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        using var _ = await locks.AcquireAsync(LockKey(id), ct);
        var none = CancellationToken.None;
        var efp = await LoadAsync(id, none);
        if (efp.Status == MarketEfpStatus.Canceled)
            throw new BusinessException($"O EFP nº {id} já está cancelado.");

        // CanceledBy sem Status Canceled = cancelamento em andamento: a retomada recusa, para não recriar nem
        // reaprovar a fixação que o cancelamento já desfez. Chamar o cancelamento de novo continua de onde parou.
        var alreadyStarted = efp.CanceledBy is not null;
        efp.CanceledBy = userName;
        await context.SaveChangesAsync(none);

        var fixationStatus = await FixationStatusAsync(efp, none);
        if (fixationStatus == PriceFixationStatus.Confirmed)
        {
            try
            {
                await RunFixationAsync(() => efp.ContractSide == MarketEfpContractSide.Sales
                    ? fixations.SalesReverse.ExecuteAsync(efp.FixationKey!.Value, userName)
                    : fixations.PurchaseReverse.ExecuteAsync(efp.FixationKey!.Value, userName));
            }
            catch (BusinessException) when (!alreadyStarted)
            {
                // Estorno recusado (baixa/composição no título, washout ativo): o EFP fica como estava.
                context.ChangeTracker.Clear();
                var untouched = await LoadAsync(id, none);
                untouched.CanceledBy = null;
                await context.SaveChangesAsync(none);
                throw;
            }

            fixationStatus = PriceFixationStatus.InApproval;
        }

        if (fixationStatus == PriceFixationStatus.InApproval)
            await RunFixationAsync(() => efp.ContractSide == MarketEfpContractSide.Sales
                ? fixations.SalesDelete.ExecuteAsync(efp.FixationKey!.Value, userName)
                : fixations.PurchaseDelete.ExecuteAsync(efp.FixationKey!.Value, userName));

        var trade = await context.MarketFuturesTrades.AsNoTracking().Where(t => t.EfpId == id)
            .Select(t => new { t.Id, t.Confirmed }).FirstOrDefaultAsync(none);
        if (trade is not null)
        {
            // A operação gerada pelo EFP sai junto com ele, mesmo que já conferida com a corretora: desmarcar a
            // conferência faz parte do cancelamento do EFP (a exclusão direta recusa operação conferida).
            if (trade.Confirmed)
                await trades.SetConfirmedAsync(trade.Id, false, userName, none);
            await trades.DeleteForEfpAsync(trade.Id, id, userName, none); // refaz o PEPS do par
        }

        efp = await LoadAsync(id, none);
        efp.Status = MarketEfpStatus.Canceled;
        efp.CanceledBy = userName;
        efp.CanceledAt = time.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(none);
        return efp;
    }

    /// <summary>
    /// Contratos aprovados a fixar, em KG, com saldo a fixar &gt; 0 (máx. 50, por código). O saldo vem das colunas
    /// persistidas (<c>TotalVolume − FixedVolume − WashedOutUnfixedVolume</c>); a gravação recalcula pelas fixações.
    /// </summary>
    public async Task<List<MarketEfpContractOption>> ListContractsAsync(MarketEfpContractSide side, string? search,
        CancellationToken ct)
    {
        var s = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLower();
        if (side == MarketEfpContractSide.Sales)
            return await context.SalesContracts.AsNoTracking()
                .Where(c => c.Status == ContractStatus.Approved && c.Type == ContractType.ToBeDetermined
                            && c.UnitOfMeasureCode == "KG"
                            && c.TotalVolume - c.FixedVolume - c.WashedOutUnfixedVolume > 0
                            && (s == null || c.Code!.ToLower().Contains(s) || c.CardName!.ToLower().Contains(s)))
                .OrderBy(c => c.Code).Take(50)
                .Select(c => new MarketEfpContractOption(c.Key, c.Code, c.CardName, c.ItemName,
                    c.StandardCurrency ?? CurrencyType.Brl, c.TotalVolume - c.FixedVolume - c.WashedOutUnfixedVolume,
                    c.StandardCashFlowDate))
                .ToListAsync(ct);
        return await context.PurchaseContracts.AsNoTracking()
            .Where(c => c.Status == ContractStatus.Approved && c.Type == ContractType.ToBeDetermined
                        && c.UnitOfMeasureCode == "KG"
                        && c.TotalVolume - c.FixedVolume - c.WashedOutUnfixedVolume > 0
                        && (s == null || c.Code!.ToLower().Contains(s) || c.CardName!.ToLower().Contains(s)))
            .OrderBy(c => c.Code).Take(50)
            .Select(c => new MarketEfpContractOption(c.Key, c.Code, c.CardName, c.ItemName,
                c.StandardCurrency ?? CurrencyType.Brl, c.TotalVolume - c.FixedVolume - c.WashedOutUnfixedVolume,
                c.StandardCashFlowDate))
            .ToListAsync(ct);
    }

    /// <summary>Preço da fixação por kg (8 casas): US$ → (preço + prêmio) × TonFactor × câmbio / 1000; R$/sc → / 60.</summary>
    public static decimal FixationPricePerKg(MarketQuoteUnit unit, decimal tonFactor, decimal price, decimal premium,
        decimal fxRate)
    {
        var raw = MarketUnits.QuoteCurrency(unit) == CurrencyType.Usd
            ? (price + premium) * tonFactor * fxRate / 1000m
            : (price + premium) / KgPerBag;
        return Math.Round(raw, 8, MidpointRounding.AwayFromZero);
    }

    private string LockKey(int id) => $"market-efp:{companyContext.CompanyCode}:{id}";

    private async Task<MarketEfp> LoadAsync(int id, CancellationToken ct)
    {
        var efp = await context.MarketEfps.FirstOrDefaultAsync(e => e.Id == id, ct)
                  ?? throw new KeyNotFoundException($"EFP nº {id} não encontrado.");
        await context.Entry(efp).ReloadAsync(ct);
        return efp;
    }

    private async Task<MarketEfp> RunLockedAsync(int id, string userName, CancellationToken ct)
    {
        using var _ = await locks.AcquireAsync(LockKey(id), ct);
        var none = CancellationToken.None;
        var efp = await LoadAsync(id, none);
        if (efp.Status == MarketEfpStatus.Canceled)
            throw new BusinessException($"O EFP nº {id} está cancelado.");
        if (efp.CanceledBy is not null)
            throw new BusinessException($"O cancelamento do EFP nº {id} foi iniciado; conclua o cancelamento.");
        if (efp.Status == MarketEfpStatus.Active)
            return efp;

        // 1. Operação de bolsa (única por EFP).
        var tradeId = await context.MarketFuturesTrades.AsNoTracking().Where(t => t.EfpId == id)
            .Select(t => (int?)t.Id).FirstOrDefaultAsync(none);
        if (tradeId is null)
        {
            var sales = efp.ContractSide == MarketEfpContractSide.Sales;
            var trade = await trades.CreateForEfpAsync(new MarketFuturesTradeInput(
                TradedAt(efp.Date), efp.AccountId, efp.InstrumentCode, efp.Side, efp.Lots, efp.Price, 0m,
                CurrencyType.Brl, MarketFuturesTradeReason.Efp, null,
                sales ? null : efp.ContractKey, sales ? efp.ContractKey : null, $"EFP nº {id}"), id, userName, none);
            tradeId = trade.Id;
            efp = await LoadAsync(id, none);
        }

        if (efp.FuturesTradeId != tradeId)
        {
            efp.FuturesTradeId = tradeId;
            await context.SaveChangesAsync(none);
        }

        // 2. Fixação pela chave pré-atribuída. 3. Aprovação (gera o título provisório).
        var status = await FixationStatusAsync(efp, none);
        if (status is null)
        {
            await CreateFixationAsync(efp, userName);
            status = PriceFixationStatus.InApproval;
        }

        switch (status)
        {
            case PriceFixationStatus.InApproval:
                var comment = $"Aprovado pelo EFP nº {id}";
                await RunFixationAsync(() => efp.ContractSide == MarketEfpContractSide.Sales
                    ? fixations.SalesApproval.ExecuteAsync(efp.FixationKey!.Value, comment, userName)
                    : fixations.PurchaseApproval.ExecuteAsync(efp.FixationKey!.Value, comment, userName));
                break;
            case PriceFixationStatus.Confirmed:
                break;
            default:
                throw new BusinessException(
                    $"A fixação do EFP nº {id} foi rejeitada ou cancelada no contrato {efp.ContractCode}; " +
                    "cancele o EFP.");
        }

        // 4. Concluído.
        efp = await LoadAsync(id, none);
        efp.Status = MarketEfpStatus.Active;
        await context.SaveChangesAsync(none);
        return efp;
    }

    private DateTime TradedAt(DateTime date)
    {
        var day = DateOnly.FromDateTime(date);
        if (day == SaoPauloTime.Today(time))
            return time.GetUtcNow().UtcDateTime;
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified), SaoPauloTime.Zone);
    }

    private async Task<PriceFixationStatus?> FixationStatusAsync(MarketEfp efp, CancellationToken ct)
    {
        var key = efp.FixationKey ?? throw new InvalidOperationException("EFP sem chave de fixação.");
        return efp.ContractSide == MarketEfpContractSide.Sales
            ? await context.SalesContractsPriceFixations.AsNoTracking().Where(f => f.Key == key)
                .Select(f => (PriceFixationStatus?)f.Status).FirstOrDefaultAsync(ct)
            : await context.PurchaseContractsPriceFixations.AsNoTracking().Where(f => f.Key == key)
                .Select(f => (PriceFixationStatus?)f.Status).FirstOrDefaultAsync(ct);
    }

    private Task CreateFixationAsync(MarketEfp efp, string userName)
    {
        var key = efp.FixationKey!.Value;
        var details = $"EFP nº {efp.Id}";
        // O serviço de criação não sobrescreve a chave: a entidade nasce com a chave pré-atribuída do EFP.
        return RunFixationAsync(() => efp.ContractSide == MarketEfpContractSide.Sales
            ? fixations.SalesCreate.ExecuteAsync(efp.ContractKey, new SalesContractPriceFixation
            {
                Key = key, FixationVolume = efp.VolumeKg, FixationPrice = efp.FixationPrice, FixationDate = efp.Date,
                FinancialDueDate = efp.FinancialDueDate, PaymentDetails = details, FreightCost = 0m,
            }, userName)
            : fixations.PurchaseCreate.ExecuteAsync(efp.ContractKey, new PurchaseContractPriceFixation
            {
                Key = key, FixationVolume = efp.VolumeKg, FixationPrice = efp.FixationPrice, FixationDate = efp.Date,
                FinancialDueDate = efp.FinancialDueDate, PaymentDetails = details, FreightCost = 0m,
            }, userName));
    }

    /// <summary>Os serviços de fixação lançam ApplicationException/NotFoundException; aqui viram 400 pt-BR.</summary>
    private static async Task RunFixationAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (e is ApplicationException or NotFoundException)
        {
            throw new BusinessException(e.Message);
        }
    }

    private async Task<Validated> ValidateAsync(MarketEfpInput input, CancellationToken ct)
    {
        if (input.Date == default)
            throw new BusinessException("Informe a data do EFP.");
        if (input.Date > SaoPauloTime.Today(time))
            throw new BusinessException("A data do EFP não pode estar no futuro.");
        if (!Enum.IsDefined(input.Side))
            throw new BusinessException("Informe se a operação de bolsa é de compra ou de venda.");
        if (!Enum.IsDefined(input.ContractSide))
            throw new BusinessException("Informe se o contrato é de compra ou de venda.");
        if (input.Lots <= 0)
            throw new BusinessException("A quantidade de lotes deve ser maior que zero.");
        if (input.Price <= 0)
            throw new BusinessException("O preço deve ser maior que zero.");
        var volume = Math.Round(input.VolumeKg, 3, MidpointRounding.AwayFromZero);
        if (volume <= 0)
            throw new BusinessException("O volume do EFP deve ser maior que zero.");
        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        if (notes?.Length > 500)
            throw new BusinessException("As observações devem ter no máximo 500 caracteres.");

        var account = await context.MarketCounterpartyAccounts.AsNoTracking().Include(a => a.Counterparty)
                          .FirstOrDefaultAsync(a => a.Id == input.AccountId, ct)
                      ?? throw new BusinessException("Conta não encontrada.");
        if (!account.Active)
            throw new BusinessException("A conta está inativa.");
        if (account.Counterparty is not { Active: true, Type: MarketCounterpartyType.Broker })
            throw new BusinessException("EFP só vale para conta de uma corretora ativa.");

        var code = input.InstrumentCode?.Trim() ?? string.Empty;
        if (code.Length == 0)
            throw new BusinessException("Informe o instrumento.");
        var instrument = await context.MarketInstruments.AsNoTracking().FirstOrDefaultAsync(i => i.Code == code, ct)
                         ?? throw new BusinessException($"Instrumento {code} não encontrado.");
        if (!instrument.Active)
            throw new BusinessException($"O instrumento {instrument.Code} está inativo.");
        if (instrument.Type != MarketInstrumentType.Future)
            throw new BusinessException($"O instrumento {instrument.Code} não é um contrato futuro.");
        if (instrument.QuoteUnit == MarketQuoteUnit.BrlPerUsd)
            throw new BusinessException("EFP não se aplica ao dólar futuro.");
        MarketUnits.EnsureTradable(instrument);

        var contract = await LoadContractAsync(input.ContractSide, input.ContractKey, ct)
                       ?? throw new BusinessException("Contrato não encontrado.");
        if (contract.Status != ContractStatus.Approved)
            throw new BusinessException($"O contrato {contract.Code} precisa estar aprovado.");
        if (contract.Type != ContractType.ToBeDetermined)
            throw new BusinessException($"EFP só vale para contrato a fixar; o contrato {contract.Code} é de preço fixo.");
        if (!string.Equals(contract.UnitOfMeasureCode?.Trim(), "KG", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException($"EFP só vale para contrato em KG; o contrato {contract.Code} está em {contract.UnitOfMeasureCode}.");

        // Mesma conta do serviço de criação da fixação: fixações em aprovação ou confirmadas + washout não fixado.
        var fixedVolume = input.ContractSide == MarketEfpContractSide.Sales
            ? await context.SalesContractsPriceFixations.Where(f => f.SalesContractKey == contract.Key
                    && (f.Status == PriceFixationStatus.InApproval || f.Status == PriceFixationStatus.Confirmed))
                .SumAsync(f => f.FixationVolume, ct)
            : await context.PurchaseContractsPriceFixations.Where(f => f.PurchaseContractKey == contract.Key
                    && (f.Status == PriceFixationStatus.InApproval || f.Status == PriceFixationStatus.Confirmed))
                .SumAsync(f => f.FixationVolume, ct);
        var available = contract.TotalVolume - decimal.Round(fixedVolume, 3) - contract.WashedOutUnfixedVolume;
        if (volume > available)
            throw new BusinessException(
                $"O volume excede o saldo a fixar do contrato {contract.Code}. Disponível: {available:N3} kg.");

        var currency = contract.Currency ?? CurrencyType.Brl;
        var quoteCurrency = MarketUnits.QuoteCurrency(instrument.QuoteUnit);
        decimal? storedFx = null;
        var fx = 1m;
        if (quoteCurrency == CurrencyType.Usd && currency == CurrencyType.Brl)
        {
            if (input.FxRate is not > 0)
                throw new BusinessException("Instrumento em dólar com contrato em reais exige o câmbio.");
            storedFx = input.FxRate;
            fx = input.FxRate.Value;
        }
        else if (quoteCurrency == CurrencyType.Brl && currency == CurrencyType.Usd)
            throw new BusinessException("Instrumento cotado em reais não pode fixar contrato em dólar.");

        var due = input.FinancialDueDate?.ToDateTime(TimeOnly.MinValue) ?? contract.CashFlowDate?.Date
                  ?? throw new BusinessException("Informe o vencimento financeiro da fixação.");

        var price = FixationPricePerKg(instrument.QuoteUnit, instrument.TonFactor, input.Price, input.Premium, fx);
        if (price <= 0)
            throw new BusinessException("O preço da fixação deve ser maior que zero.");

        var perLot = MarketUnits.TonsPerLot(instrument.QuoteUnit, instrument.ContractSize, instrument.TonFactor);
        decimal? tons = perLot is { } t
            ? Math.Round(t * input.Lots * (input.Side == MarketTradeSide.Buy ? 1 : -1), 4, MidpointRounding.AwayFromZero)
            : null;

        return new Validated(input, instrument.Code, contract, currency, storedFx, volume, price, due, tons, notes);
    }

    private async Task<ContractInfo?> LoadContractAsync(MarketEfpContractSide side, Guid key, CancellationToken ct) =>
        side == MarketEfpContractSide.Sales
            ? await context.SalesContracts.AsNoTracking().Where(c => c.Key == key)
                .Select(c => new ContractInfo(c.Key, c.Code, c.Status, c.Type, c.UnitOfMeasureCode, c.TotalVolume,
                    c.WashedOutUnfixedVolume, c.StandardCurrency, c.StandardCashFlowDate)).FirstOrDefaultAsync(ct)
            : await context.PurchaseContracts.AsNoTracking().Where(c => c.Key == key)
                .Select(c => new ContractInfo(c.Key, c.Code, c.Status, c.Type, c.UnitOfMeasureCode, c.TotalVolume,
                    c.WashedOutUnfixedVolume, c.StandardCurrency, c.StandardCashFlowDate)).FirstOrDefaultAsync(ct);
}
