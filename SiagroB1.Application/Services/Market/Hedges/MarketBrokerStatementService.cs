using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>Extrato diário da corretora (ajuste e margem, informativos). CreatedBy/At são do servidor.</summary>
public class MarketBrokerStatementService(AppDbContext context, ILogger<MarketBrokerStatementService> logger)
    : BaseService<MarketBrokerStatement, int>(context, logger)
{
    public override Task<MarketBrokerStatement> CreateAsync(MarketBrokerStatement entity) => CreateAsync(entity, null);

    public override Task<MarketBrokerStatement?> UpdateAsync(int key, MarketBrokerStatement entity) =>
        UpdateAsync(key, entity, null);

    public async Task<MarketBrokerStatement> CreateAsync(MarketBrokerStatement entity, string? userName)
    {
        await ValidateAsync(entity);
        entity.CreatedBy = userName;
        entity.CreatedAt = DateTime.UtcNow;
        return await MarketPersistence.AddAndSaveAsync(context, entity);
    }

    public async Task<MarketBrokerStatement?> UpdateAsync(int key, MarketBrokerStatement entity, string? userName)
    {
        entity.Id = key;
        var stamp = await context.MarketBrokerStatements.AsNoTracking()
            .Where(s => s.Id == key).Select(s => new { s.CreatedBy, s.CreatedAt }).FirstOrDefaultAsync();
        if (stamp is null)
            throw new KeyNotFoundException("Entity not found.");

        await ValidateAsync(entity);
        entity.CreatedBy = stamp.CreatedBy;
        entity.CreatedAt = stamp.CreatedAt;
        return await base.UpdateAsync(key, entity);
    }

    private async Task ValidateAsync(MarketBrokerStatement entity)
    {
        if (entity.Date == default)
            throw new BusinessException("Informe a data do extrato.");
        entity.Date = entity.Date.Date;

        var account = await context.MarketCounterpartyAccounts.AsNoTracking().Include(a => a.Counterparty)
            .FirstOrDefaultAsync(a => a.Id == entity.AccountId)
            ?? throw new BusinessException("Conta não encontrada.");
        if (!account.Active)
            throw new BusinessException("A conta está inativa.");
        if (account.Counterparty is not { Active: true, Type: MarketCounterpartyType.Broker })
            throw new BusinessException("O extrato só vale para conta de uma corretora ativa.");

        if (entity.MarginRequired < 0)
            throw new BusinessException("A margem exigida não pode ser negativa.");
        if (entity.MarginDeposited < 0)
            throw new BusinessException("A margem depositada não pode ser negativa.");
        if (!Enum.IsDefined(entity.Currency))
            throw new BusinessException("Informe a moeda do extrato.");

        entity.Notes = entity.Notes?.Trim();
        if (string.IsNullOrEmpty(entity.Notes))
            entity.Notes = null;
        else if (entity.Notes.Length > 500)
            throw new BusinessException("As observações devem ter no máximo 500 caracteres.");

        if (await context.MarketBrokerStatements.AsNoTracking()
                .AnyAsync(s => s.Id != entity.Id && s.AccountId == entity.AccountId && s.Date == entity.Date))
            throw new BusinessException("Já existe um extrato desta conta nesta data.");
    }
}
