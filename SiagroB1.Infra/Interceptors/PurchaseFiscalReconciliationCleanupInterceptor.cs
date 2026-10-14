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
/// <item>documento de complemento de valor cancelado, excluído ou com a confirmação estornada → firmes das linhas cancelados ou recusa;</item>
/// <item>linha vinculada com quantidade reduzida abaixo do vinculado, UM sem conversão ou produto trocado → recusa;</item>
/// <item>linha vinculada com valor (total, frete, seguro, desconto, outras, complemento, Funrural) alterado → firme solto
/// recalculado com log, não-solto recusa; contrato trocado → recusa; natureza que deixa de gerar financeiro → firme solto
/// cancelado, não-solto recusa (revisão final C1);</item>
/// <item>documento com vínculo ativo e tipo ou emitente trocado → recusa.</item>
/// </list>
/// <paramref name="isStandalone"/> vem do modo do ERP (Program.cs): em SAPB1 a natureza (OUSG) não tem a flag
/// "gera financeiro" e conta como gera (D10). O interceptor não enxerga IUsage: lê USAGES direto, que é o que o
/// UsageService do STANDALONE lê.
/// As consultas são poucas e por chave; uma implementação síncrona atende os dois caminhos do SaveChanges.
/// </summary>
public class PurchaseFiscalReconciliationCleanupInterceptor(bool isStandalone = true) : SaveChangesInterceptor
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

    private void Apply(DbContext context)
    {
        var cancelledTransactions = new HashSet<Guid>();
        var cancelledInvoices = new HashSet<Guid>();
        var deletedLines = new HashSet<Guid>();
        var changedLines = new List<EntityEntry<PurchaseInvoiceItem>>();
        // Spec 2026-10-13 D11: documento cujo firme de complemento de valor tem de morrer, com quem fez a operação.
        var undoneInvoices = new Dictionary<Guid, string>();
        // Revisão final (C1): cabeçalho com tipo ou emitente trocado.
        var headerChanged = new List<Guid>();

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
                    undoneInvoices[i.Key] = i.CanceledBy ?? PurchaseFirmPayableRules.SystemUserName;
                    break;
                case PurchaseInvoice i when ConfirmationReversed(entry):
                    undoneInvoices[i.Key] = i.UpdatedBy ?? PurchaseFirmPayableRules.SystemUserName;
                    if (HeaderChanged(entry)) headerChanged.Add(i.Key);
                    break;
                case PurchaseInvoice i when HeaderChanged(entry):
                    headerChanged.Add(i.Key);
                    break;
                case PurchaseInvoiceItem { Key: { } key } when entry.State == EntityState.Deleted:
                    deletedLines.Add(key);
                    break;
                case PurchaseInvoiceItem { Key: not null } item when entry.State == EntityState.Modified:
                    changedLines.Add(context.Entry(item));
                    break;
            }
        }

        if (cancelledTransactions.Count + cancelledInvoices.Count + deletedLines.Count + changedLines.Count +
            undoneInvoices.Count + headerChanged.Count == 0)
            return;

        var links = context.Set<PurchaseFiscalReconciliation>();

        foreach (var invoiceKey in headerChanged)
            EnsureHeaderEditKeepsLinks(links, invoiceKey);

        foreach (var line in changedLines)
            EnsureLineEditKeepsLinks(context, links, line);

        // Revisão final (C1): o firme acompanha a linha conciliada. Linha de NF que está sendo cancelada fica com a
        // limpeza abaixo; vínculo removido neste mesmo SaveChanges não conta.
        var removedLinks = context.ChangeTracker.Entries<PurchaseFiscalReconciliation>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Entity.Key)
            .ToHashSet();
        foreach (var line in changedLines.Where(l => l.Entity.PurchaseInvoiceKey is not { } k || !cancelledInvoices.Contains(k)))
            SyncFirmsWithLineEdit(context, links, line, removedLinks, cancelledTransactions);

        // D11: o firme do complemento de valor segue o documento (cancelado, excluído ou com a confirmação estornada).
        if (undoneInvoices.Count > 0)
        {
            var invoiceKeys = undoneInvoices.Keys.ToList();
            var linesByInvoice = context.Set<PurchaseInvoiceItem>().AsNoTracking()
                .Where(l => l.PurchaseInvoiceKey != null && invoiceKeys.Contains(l.PurchaseInvoiceKey.Value))
                .Select(l => new { InvoiceKey = l.PurchaseInvoiceKey!.Value, LineKey = l.Key!.Value })
                .ToList()
                .GroupBy(l => l.InvoiceKey);

            foreach (var group in linesByInvoice)
                PurchaseFirmPayableRules.CancelValueComplementFirms(
                    context, group.Select(l => l.LineKey).ToList(), undoneInvoices[group.Key]);
        }

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

    /// <summary>Estorno da confirmação: Confirmed → Pending (PurchaseInvoicesReverseConfirmService).</summary>
    private static bool ConfirmationReversed(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified) return false;
        var p = entry.Property(nameof(PurchaseInvoice.InvoiceStatus));
        return p.IsModified && Equals(p.OriginalValue, InvoiceStatus.Confirmed) && Equals(p.CurrentValue, InvoiceStatus.Pending);
    }

    private static bool BecameCancelled<TStatus>(EntityEntry entry, string property, TStatus cancelled) where TStatus : struct, Enum
    {
        if (entry.State != EntityState.Modified) return false;
        var p = entry.Property(property);
        return p.IsModified && Equals(p.CurrentValue, cancelled) && !Equals(p.OriginalValue, cancelled);
    }

    /// <summary>Tipo ou emitente trocado: o sentido e o fornecedor dos vínculos deixariam de casar com os romaneios.</summary>
    private static bool HeaderChanged(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified) return false;
        var type = entry.Property(nameof(PurchaseInvoice.InvoiceType));
        var card = entry.Property(nameof(PurchaseInvoice.CardCode));
        return (type.IsModified && !Equals(type.OriginalValue, type.CurrentValue)) ||
               (card.IsModified && !string.Equals((string?)card.OriginalValue, (string?)card.CurrentValue, StringComparison.Ordinal));
    }

    private static void EnsureHeaderEditKeepsLinks(DbSet<PurchaseFiscalReconciliation> links, Guid invoiceKey)
    {
        var linked = links.AsNoTracking().Any(l =>
            l.PurchaseInvoiceItem!.PurchaseInvoiceKey == invoiceKey &&
            l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled);
        if (linked)
            throw new DefaultException(
                "Documento conciliado a romaneio: desfaça a conciliação antes de alterar o tipo ou o emitente.");
    }

    /// <summary>
    /// Revisão final (C1): linha vinculada editada depois do vínculo. Contrato trocado recusa sempre; natureza que deixa
    /// de gerar financeiro cancela o firme solto (não-solto recusa, S1/P1); valor alterado recalcula o firme solto pela
    /// regra 4, com log (não-solto recusa). Lê os valores finais da entidade: vale para qualquer caminho de gravação.
    /// </summary>
    private void SyncFirmsWithLineEdit(
        DbContext context, DbSet<PurchaseFiscalReconciliation> links, EntityEntry<PurchaseInvoiceItem> entry,
        HashSet<Guid> removedLinks, HashSet<Guid> cancelledTransactions)
    {
        var contract = entry.Property(x => x.PurchaseContractKey);
        var usage = entry.Property(x => x.UsageCode);
        var contractChanged = contract.IsModified && contract.OriginalValue != contract.CurrentValue;
        var usageChanged = usage.IsModified && usage.OriginalValue != usage.CurrentValue;

        // GrandTotal e FunruralValue são derivados (sem coluna): compara os da cópia com os valores originais.
        var original = (PurchaseInvoiceItem)entry.OriginalValues.ToObject();
        var line = entry.Entity;
        var valueChanged = original.GrandTotal != line.GrandTotal || original.FunruralValue != line.FunruralValue ||
                           original.Quantity != line.Quantity ||
                           !string.Equals(original.UnitOfMeasureCode, line.UnitOfMeasureCode, StringComparison.Ordinal);

        if (!contractChanged && !usageChanged && !valueChanged)
            return;

        var key = line.Key!.Value;
        var active = links.AsNoTracking()
            .Where(l => l.PurchaseInvoiceItemKey == key && l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .Select(l => new { l.Key, l.Quantity, l.StorageTransactionKey })
            .ToList()
            .Where(l => !removedLinks.Contains(l.Key) && !cancelledTransactions.Contains(l.StorageTransactionKey))
            .ToList();
        if (active.Count == 0)
            return;

        if (contractChanged)
            throw new DefaultException("Linha conciliada a romaneio: desfaça a conciliação antes de trocar o contrato.");

        var userName = UserOf(context, line.PurchaseInvoiceKey);

        if (usageChanged && !CreatesFinancialDocument(context, line.UsageCode))
        {
            PurchaseFirmPayableRules.CancelLooseFirms(
                context, active.Select(l => l.Key).ToList(), userName, PurchaseFirmPayableRules.NatureWithoutFinancialReason);
            return;
        }

        if (!valueChanged)
            return;

        var complement = context.Set<ItemComplement>().AsNoTracking().FirstOrDefault(c => c.ItemCode == line.ItemCode);
        var factor = R.KgFactor(line.UnitOfMeasureCode, complement);
        if (factor is null)
            return; // Só sem vínculo ativo chega aqui sem fator: EnsureLineEditKeepsLinks recusa a UM sem conversão.

        var lineKg = line.Quantity * factor.Value;
        foreach (var link in active)
        foreach (var firm in PurchaseFirmPayableRules.ActiveFirmsOf(context, [link.Key]))
        {
            var amount = PurchaseFirmPayableRules.Amount(link.Quantity, lineKg, line.GrandTotal, line.FunruralValue);
            PurchaseFirmPayableRules.RecalculateLooseFirm(context, firm, amount, userName);
        }
    }

    /// <summary>D10: "gera financeiro" da natureza; SAPB1 conta como gera; linha sem natureza não gera.</summary>
    private bool CreatesFinancialDocument(DbContext context, int? usageCode)
    {
        if (usageCode is not { } code) return false;
        if (!isStandalone) return true;
        return context.Set<Usage>().AsNoTracking()
            .Where(u => u.Code == code)
            .Select(u => u.CreatesFinancialDocument)
            .FirstOrDefault();
    }

    /// <summary>
    /// Quem alterou: o UpdatedBy do documento quando carimbado nesta gravação (PurchaseInvoicesUpdateService). A edição
    /// só da linha (PATCH em PurchaseInvoicesItems) não carimba o documento: fica "Sistema".
    /// </summary>
    private static string UserOf(DbContext context, Guid? invoiceKey)
    {
        var invoice = context.ChangeTracker.Entries<PurchaseInvoice>().FirstOrDefault(e => e.Entity.Key == invoiceKey);
        if (invoice is { State: EntityState.Modified } && invoice.Property(x => x.UpdatedBy).IsModified &&
            !string.IsNullOrWhiteSpace(invoice.Entity.UpdatedBy))
            return invoice.Entity.UpdatedBy!;
        return PurchaseFirmPayableRules.SystemUserName;
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
