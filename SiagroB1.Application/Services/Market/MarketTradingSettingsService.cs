using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

/// <summary>Corpo de leitura e gravação dos parâmetros de negociação.</summary>
public sealed record MarketTradingSettingsInput(
    string? SpotFxInstrumentCode,
    decimal? BoardApprovalVolumeTons,
    decimal? BoardApprovalDeviationPercent,
    string? ContractUnitOfMeasureCode,
    string? ContractFreightUmCode);

/// <summary>
/// Parâmetros gerais do Mercado (linha única). A permissão é checada aqui, e não no controller, porque depende
/// de QUAIS campos mudaram: limiares de alçada exigem MARKET_BOARD_APPROVE; o resto, MARKET_MANAGE.
/// </summary>
public class MarketTradingSettingsService(
    AppDbContext context, MarketPermissionGuard guard, LiveQuoteStore store, ICompanyContext companyContext)
{
    public async Task<MarketTradingSettingsInput> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.MarketTradingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return ToInput(row);
    }

    public async Task<MarketTradingSettingsInput> SaveAsync(
        MarketTradingSettingsInput input, string userName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SaveCoreAsync(input, userName, cancellationToken);
        }
        catch (DbUpdateException e) when (MarketQuoteService.IsUniqueViolation(e))
        {
            // Corrida na primeira gravação (linha única Key = 1): outro pedido inseriu antes; repete uma vez como UPDATE.
            context.ChangeTracker.Clear();
            return await SaveCoreAsync(input, userName, cancellationToken);
        }
    }

    private async Task<MarketTradingSettingsInput> SaveCoreAsync(
        MarketTradingSettingsInput input, string userName, CancellationToken cancellationToken)
    {
        var row = await context.MarketTradingSettings.FirstOrDefaultAsync(cancellationToken);
        var current = ToInput(row);

        var normalized = new MarketTradingSettingsInput(
            Blank(input.SpotFxInstrumentCode),
            // Arredonda à escala da coluna ANTES de comparar e validar: 0,0001 t viraria 0 no banco.
            Scale(input.BoardApprovalVolumeTons, 3),
            Scale(input.BoardApprovalDeviationPercent, 4),
            Unit(input.ContractUnitOfMeasureCode, "unidade de medida do contrato"),
            Unit(input.ContractFreightUmCode, "unidade de medida do frete"));

        var boardChanged = normalized.BoardApprovalVolumeTons != current.BoardApprovalVolumeTons
                           || normalized.BoardApprovalDeviationPercent != current.BoardApprovalDeviationPercent;
        var otherChanged = !string.Equals(normalized.SpotFxInstrumentCode, current.SpotFxInstrumentCode, StringComparison.Ordinal)
                           || normalized.ContractUnitOfMeasureCode != current.ContractUnitOfMeasureCode
                           || normalized.ContractFreightUmCode != current.ContractFreightUmCode;

        if (boardChanged)
            await guard.EnsureAsync(userName, PermissionCodes.MarketBoardApprove);
        if (otherChanged)
            await guard.EnsureAsync(userName, PermissionCodes.MarketManage);
        if (!boardChanged && !otherChanged)
            return current;

        if (normalized.BoardApprovalVolumeTons <= 0)
            throw new BusinessException("O volume de alçada deve ser maior que zero.");
        if (normalized.BoardApprovalDeviationPercent <= 0)
            throw new BusinessException("O desvio de alçada deve ser maior que zero.");

        if (normalized.ContractUnitOfMeasureCode is not null
            && normalized.ContractUnitOfMeasureCode != MarketTradeTicketContractService.ContractUnitOfMeasure)
            throw new BusinessException(MarketTradeTicketContractService.ContractUnitMessage);

        if (normalized.SpotFxInstrumentCode is not null)
        {
            var fx = await context.MarketInstruments.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Code == normalized.SpotFxInstrumentCode, cancellationToken)
                ?? throw new BusinessException("Instrumento do dólar spot não encontrado.");
            if (!fx.Active || fx.QuoteUnit != MarketQuoteUnit.BrlPerUsd)
                throw new BusinessException("O dólar spot deve ser um instrumento ativo cotado em R$ por US$.");
            normalized = normalized with { SpotFxInstrumentCode = fx.Code };
        }

        if (row is null)
        {
            row = new MarketTradingSettings();
            context.MarketTradingSettings.Add(row);
        }

        row.SpotFxInstrumentCode = normalized.SpotFxInstrumentCode;
        row.BoardApprovalVolumeTons = normalized.BoardApprovalVolumeTons;
        row.BoardApprovalDeviationPercent = normalized.BoardApprovalDeviationPercent;
        row.ContractUnitOfMeasureCode = normalized.ContractUnitOfMeasureCode;
        row.ContractFreightUmCode = normalized.ContractFreightUmCode;
        await context.SaveChangesAsync(cancellationToken);
        store.NotifyChanged(companyContext.CompanyCode);
        return normalized;
    }

    private static MarketTradingSettingsInput ToInput(MarketTradingSettings? row) => new(
        row?.SpotFxInstrumentCode, row?.BoardApprovalVolumeTons, row?.BoardApprovalDeviationPercent,
        row?.ContractUnitOfMeasureCode, row?.ContractFreightUmCode);

    private static decimal? Scale(decimal? value, int decimals) =>
        value is { } v ? Math.Round(v, decimals, MidpointRounding.AwayFromZero) : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Unit(string? value, string name)
    {
        var unit = Blank(value)?.ToUpperInvariant();
        if (unit?.Length > 4)
            throw new BusinessException($"A {name} deve ter no máximo 4 caracteres.");
        return unit;
    }
}
