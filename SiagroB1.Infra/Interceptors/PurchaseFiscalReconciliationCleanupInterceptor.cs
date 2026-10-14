using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Infra.Interceptors;

/// <summary>
/// Conciliação Fiscal de Compras (spec 2026-10-13 §4.2): a conciliação é posterior e NUNCA trava o fluxo. Num ponto só,
/// para todos os caminhos (6 serviços cancelam romaneio, cancelamento/exclusão da NF, SyncItems, PATCH direto):
/// <list type="bullet">
/// <item>romaneio cancelado/excluído, NF cancelada/excluída ou linha excluída → vínculos removidos e a confirmação manual
/// dos romaneios que ficaram é limpa;</item>
/// <item>linha vinculada com quantidade reduzida abaixo do vinculado, UM sem conversão ou produto trocado → recusa.</item>
/// </list>
/// As consultas são poucas e por chave; uma implementação síncrona atende os dois caminhos do SaveChanges.
/// </summary>
public class PurchaseFiscalReconciliationCleanupInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext context)
    {
        var cancelledTransactions = new HashSet<Guid>();
        var cancelledInvoices = new HashSet<Guid>();
        var deletedLines = new HashSet<Guid>();
        var changedLines = new List<EntityEntry<PurchaseInvoiceItem>>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case StorageTransaction t when entry.State == EntityState.Deleted ||
                                               BecameCancelled(entry, nameof(StorageTransaction.TransactionStatus), StorageTransactionsStatus.Cancelled):
                    cancelledTransactions.Add(t.Key);
                    break;
                case PurchaseInvoice i when entry.State == EntityState.Deleted ||
                                            BecameCancelled(entry, nameof(PurchaseInvoice.InvoiceStatus), InvoiceStatus.Cancelled):
                    cancelledInvoices.Add(i.Key);
                    break;
                case PurchaseInvoiceItem { Key: { } key } when entry.State == EntityState.Deleted:
                    deletedLines.Add(key);
                    break;
                case PurchaseInvoiceItem { Key: not null } item when entry.State == EntityState.Modified:
                    changedLines.Add(context.Entry(item));
                    break;
            }
        }

        if (cancelledTransactions.Count + cancelledInvoices.Count + deletedLines.Count + changedLines.Count == 0)
            return;

        var links = context.Set<PurchaseFiscalReconciliation>();

        foreach (var line in changedLines)
            EnsureLineEditKeepsLinks(context, links, line);

        if (cancelledTransactions.Count + cancelledInvoices.Count + deletedLines.Count == 0)
            return;

        if (cancelledInvoices.Count > 0)
            deletedLines.UnionWith(context.Set<PurchaseInvoiceItem>().AsNoTracking()
                .Where(l => l.PurchaseInvoiceKey != null && cancelledInvoices.Contains(l.PurchaseInvoiceKey.Value))
                .Select(l => l.Key!.Value)
                .ToList());

        var doomed = links
            .Where(l => cancelledTransactions.Contains(l.StorageTransactionKey) || deletedLines.Contains(l.PurchaseInvoiceItemKey))
            .ToList();
        if (doomed.Count == 0)
            return;

        // Spec 2026-10-13 (títulos firmes, regra 7): o firme solto do vínculo é cancelado; o composto (ou com dação ou
        // encontro de contas) recusa a operação inteira — antes de remover qualquer vínculo.
        PurchaseFirmPayableRules.CancelLooseFirms(
            context, doomed.Select(l => l.Key).ToList(), PurchaseFirmPayableRules.SystemUserName);

        links.RemoveRange(doomed);

        var touched = doomed.Select(l => l.StorageTransactionKey).Where(k => !cancelledTransactions.Contains(k)).Distinct().ToList();
        foreach (var tx in context.Set<StorageTransaction>().Where(t => touched.Contains(t.Key) && t.FiscalReconciledAt != null).ToList())
        {
            tx.FiscalReconciledAt = null;
            tx.FiscalReconciledBy = null;
        }
    }

    private static bool BecameCancelled<TStatus>(EntityEntry entry, string property, TStatus cancelled) where TStatus : struct, Enum
    {
        if (entry.State != EntityState.Modified) return false;
        var p = entry.Property(property);
        return p.IsModified && Equals(p.CurrentValue, cancelled) && !Equals(p.OriginalValue, cancelled);
    }

    private static void EnsureLineEditKeepsLinks(DbContext context, DbSet<PurchaseFiscalReconciliation> links, EntityEntry<PurchaseInvoiceItem> entry)
    {
        var quantity = entry.Property(x => x.Quantity);
        var item = entry.Property(x => x.ItemCode);
        var unit = entry.Property(x => x.UnitOfMeasureCode);
        if (!quantity.IsModified && !item.IsModified && !unit.IsModified)
            return;

        var key = entry.Entity.Key!.Value;
        var linked = links.AsNoTracking()
            .Where(l => l.PurchaseInvoiceItemKey == key && l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .Sum(l => (decimal?)l.Quantity) ?? 0m;
        if (linked == 0)
            return;

        if (item.IsModified && !string.Equals(item.OriginalValue, item.CurrentValue, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException(
                "A linha da NF está vinculada a romaneios na Conciliação Fiscal de Compras; desvincule antes de trocar o produto.");

        var complement = context.Set<ItemComplement>().AsNoTracking().FirstOrDefault(c => c.ItemCode == entry.Entity.ItemCode);
        var factor = R.KgFactor(entry.Entity.UnitOfMeasureCode, complement);
        if (factor is null || entry.Entity.Quantity * factor.Value < linked)
            throw new DefaultException(
                $"A linha da NF tem {linked:N3} KG vinculados a romaneios; desvincule na Conciliação Fiscal de Compras antes de reduzir a quantidade ou trocar a unidade.");
    }
}
