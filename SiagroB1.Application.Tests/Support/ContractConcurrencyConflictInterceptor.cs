using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Simula o conflito de RowVersion do contrato (que o provider InMemory não reproduz): falha o
/// SaveChanges que tentar gravar um <see cref="PurchaseContract"/> alterado.
/// </summary>
public sealed class ContractConcurrencyConflictInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<PurchaseContract>().Any(e => e.State == EntityState.Modified))
            throw new DbUpdateConcurrencyException("conflito simulado no contrato");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
