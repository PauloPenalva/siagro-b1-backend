using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>Corretoras e bancos. As contas têm serviço próprio; o corpo do cabeçalho nunca as altera.</summary>
public class MarketCounterpartyService(AppDbContext context, ILogger<MarketCounterpartyService> logger)
    : BaseService<MarketCounterparty, int>(context, logger)
{
    public override async Task<MarketCounterparty> CreateAsync(MarketCounterparty entity)
    {
        await ValidateAsync(entity);
        // Inclusão em profundidade: cada conta passa pelas mesmas regras da gravação avulsa antes de qualquer SaveChanges.
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in entity.Accounts)
        {
            MarketCounterpartyAccountRules.Normalize(account);
            if (!codes.Add(account.Code))
                throw new BusinessException("Há contas com o mesmo código nesta corretora ou banco.");
        }

        return await MarketPersistence.AddAndSaveAsync(context, entity);
    }

    public override async Task<MarketCounterparty?> UpdateAsync(int key, MarketCounterparty entity)
    {
        entity.Id = key;
        var current = await context.MarketCounterparties.AsNoTracking()
            .Where(c => c.Id == key).Select(c => new { c.Type }).FirstOrDefaultAsync();
        if (current is null)
            throw new KeyNotFoundException("Entity not found.");

        await ValidateAsync(entity);
        if (current.Type != entity.Type && await IsUsedAsync(key))
            throw new BusinessException("Não é possível mudar o tipo: há contas usadas em operações, hedges, EFPs ou extratos.");

        IgnorePayloadAccounts(entity);
        return await base.UpdateAsync(key, entity);
    }

    public override async Task<bool> DeleteAsync(int key)
    {
        var counterparty = await context.MarketCounterparties.Include(c => c.Accounts).FirstOrDefaultAsync(c => c.Id == key);
        if (counterparty is null)
            return false;
        if (await IsUsedAsync(key))
            throw new BusinessException("Há contas usadas em operações, hedges, EFPs ou extratos; inative em vez de excluir.");

        context.MarketCounterpartyAccounts.RemoveRange(counterparty.Accounts);
        context.MarketCounterparties.Remove(counterparty);
        await context.SaveChangesAsync();
        return true;
    }

    private Task<bool> IsUsedAsync(int counterpartyId) => MarketAccountUsage.IsUsedAsync(context,
        context.MarketCounterpartyAccounts.Where(a => a.CounterpartyId == counterpartyId).Select(a => a.Id));

    // Em PUT a entidade vem solta e a lista é descartada; em PATCH ela já está rastreada e sai toda conta que o
    // payload trouxe ou alterou (só as rastreadas intactas ficam).
    private void IgnorePayloadAccounts(MarketCounterparty entity)
    {
        if (context.Entry(entity).State == EntityState.Detached)
        {
            entity.Accounts = [];
            return;
        }

        foreach (var account in entity.Accounts.ToList())
        {
            var entry = context.Entry(account);
            if (entry.State == EntityState.Unchanged)
                continue;

            entry.State = EntityState.Detached;
            entity.Accounts.Remove(account);
        }
    }

    private async Task ValidateAsync(MarketCounterparty entity)
    {
        entity.Name = entity.Name?.Trim() ?? string.Empty;
        if (entity.Name.Length == 0)
            throw new BusinessException("Informe o nome da corretora ou banco.");
        if (entity.Name.Length > 100)
            throw new BusinessException("O nome deve ter no máximo 100 caracteres.");
        if (!Enum.IsDefined(entity.Type))
            throw new BusinessException("Informe o tipo (corretora ou banco).");

        entity.TaxId = entity.TaxId?.Trim();
        if (string.IsNullOrEmpty(entity.TaxId))
            entity.TaxId = null;
        else if (entity.TaxId.Length > 20)
            throw new BusinessException("O CNPJ deve ter no máximo 20 caracteres.");

        var upper = entity.Name.ToUpper();
        if (await context.MarketCounterparties.AsNoTracking().AnyAsync(c => c.Id != entity.Id && c.Name.ToUpper() == upper))
            throw new BusinessException("Já existe uma corretora ou banco com este nome.");
    }
}
