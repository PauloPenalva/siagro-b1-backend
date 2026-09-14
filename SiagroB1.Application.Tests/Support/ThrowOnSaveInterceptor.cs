using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Simula, no InMemory (que não aplica índice único), a exceção que o SQL Server dispararia
/// no <c>SaveChanges</c> de duas criações/edições concorrentes disputando o índice filtrado
/// <c>IX_WAREHOUSE_RECONCILIATIONS_OpenPerWarehouseItem</c>: o guard passou (nenhuma
/// consulta prévia via <c>DbContext</c> enxerga a outra transação), e só o INSERT/UPDATE
/// em si colide.
/// </summary>
public sealed class ThrowOnSaveInterceptor(Func<EntityEntry, bool> shouldThrow) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfMatched(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ThrowIfMatched(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    private void ThrowIfMatched(DbContext? context)
    {
        if (context != null && context.ChangeTracker.Entries().Any(shouldThrow))
            throw new DbUpdateException(
                "Simulated violation of IX_WAREHOUSE_RECONCILIATIONS_OpenPerWarehouseItem.");
    }
}
