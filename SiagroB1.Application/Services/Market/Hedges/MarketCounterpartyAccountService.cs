using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>Contas de corretoras e bancos. Excluir só sem uso (operação, hedge, EFP ou extrato).</summary>
public class MarketCounterpartyAccountService(AppDbContext context, ILogger<MarketCounterpartyAccountService> logger)
    : BaseService<MarketCounterpartyAccount, int>(context, logger)
{
    public override async Task<MarketCounterpartyAccount> CreateAsync(MarketCounterpartyAccount entity)
    {
        await MarketCounterpartyAccountRules.ValidateAsync(context, entity);
        return await MarketPersistence.AddAndSaveAsync(context, entity);
    }

    public override async Task<MarketCounterpartyAccount?> UpdateAsync(int key, MarketCounterpartyAccount entity)
    {
        entity.Id = key;
        var currentCounterpartyId = await context.MarketCounterpartyAccounts.AsNoTracking()
            .Where(a => a.Id == key).Select(a => (int?)a.CounterpartyId).FirstOrDefaultAsync();
        if (currentCounterpartyId is null)
            throw new KeyNotFoundException("Entity not found.");
        if (currentCounterpartyId != entity.CounterpartyId)
            throw new BusinessException("Não é possível mover a conta para outra corretora ou banco.");

        await MarketCounterpartyAccountRules.ValidateAsync(context, entity);
        return await base.UpdateAsync(key, entity);
    }

    public override async Task<bool> DeleteAsync(int key)
    {
        if (await MarketAccountUsage.IsUsedAsync(context, context.MarketCounterpartyAccounts.Where(a => a.Id == key).Select(a => a.Id)))
            throw new BusinessException("A conta é usada em operações, hedges, EFPs ou extratos; inative-a em vez de excluir.");

        return await base.DeleteAsync(key);
    }

    public IQueryable<MarketCounterpartyAccount> QueryByCounterparty(int counterpartyId) =>
        QueryAll().Where(a => a.CounterpartyId == counterpartyId);
}
