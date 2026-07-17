using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Interceptors;

/// <summary>
/// Impede alterar (add/update/delete) sub-entidades vinculadas a um contrato de
/// compra encerrado (<see cref="ContractStatus.Finished"/>): corretores, impostos,
/// parâmetros de qualidade e fixações de preço. Cobre todos os caminhos de
/// mutação (controllers OData diretos e serviços) num único ponto.
/// </summary>
public class FinishedContractMutationGuardInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            var keys = CollectContractKeys(eventData.Context);
            if (keys.Count > 0)
            {
                var hasFinished = await eventData.Context.Set<PurchaseContract>()
                    .AsNoTracking()
                    .AnyAsync(c => keys.Contains(c.Key) && c.Status == ContractStatus.Finished, cancellationToken);

                if (hasFinished)
                    throw Blocked();
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            var keys = CollectContractKeys(eventData.Context);
            if (keys.Count > 0)
            {
                var hasFinished = eventData.Context.Set<PurchaseContract>()
                    .AsNoTracking()
                    .Any(c => keys.Contains(c.Key) && c.Status == ContractStatus.Finished);

                if (hasFinished)
                    throw Blocked();
            }
        }

        return base.SavingChanges(eventData, result);
    }

    private static HashSet<Guid> CollectContractKeys(DbContext context)
    {
        var keys = new HashSet<Guid>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            Guid? key = entry.Entity switch
            {
                PurchaseContractBroker b => b.PurchaseContractKey,
                PurchaseContractTax t => t.PurchaseContractKey,
                PurchaseContractQualityParameter q => q.PurchaseContractKey,
                PurchaseContractPriceFixation f => f.PurchaseContractKey,
                _ => null
            };

            if (key.HasValue && key.Value != Guid.Empty)
                keys.Add(key.Value);
        }

        return keys;
    }

    private static DefaultException Blocked() =>
        new("Contrato encerrado: não é possível alterar dados vinculados ao contrato.");
}
