using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.PurchaseFiscalReconciliations;

/// <summary>
/// Conciliação Fiscal de Compras (spec 2026-10-13): vínculo linha de NF do fornecedor × romaneio de compra, N:N com
/// quantidade em KG, posterior e SEM efeito em saldo. As travas de §4 moram aqui (toda gravação passa por
/// <see cref="SaveAsync"/>); a limpeza automática é do <c>PurchaseFiscalReconciliationCleanupInterceptor</c>.
/// </summary>
public class PurchaseFiscalReconciliationService(IUnitOfWork db)
{
    public Task<List<FiscalReconciliationTransactionDto>> ListTransactionsAsync(FiscalReconciliationFilter filter)
    {
        EnsureFilter(filter);
        return PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(db.Context, filter);
    }

    public Task<List<FiscalReconciliationInvoiceLineDto>> ListInvoiceLinesAsync(FiscalReconciliationFilter filter)
    {
        EnsureFilter(filter);
        return PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(db.Context, filter);
    }

    public Task<List<FiscalReconciliationLinkDto>> ListLinksAsync(Guid transactionKey) =>
        PurchaseFiscalReconciliationQuery.LoadLinksAsync(db.Context, [transactionKey]);

    public async Task<List<FiscalReconciliationPairDto>> SuggestAsync(IReadOnlyList<Guid> invoiceItemKeys, IReadOnlyList<Guid> transactionKeys)
    {
        if (invoiceItemKeys.Count == 0 || transactionKeys.Count == 0)
            throw new DefaultException("Selecione ao menos uma linha de NF e um romaneio.");

        var lines = await LoadLinesAsync(invoiceItemKeys.Distinct().ToList());
        var transactions = await LoadTransactionsAsync(transactionKeys.Distinct().ToList());
        var links = await PurchaseFiscalReconciliationQuery.ActiveLinks(db.Context)
            .Where(l => invoiceItemKeys.Contains(l.PurchaseInvoiceItemKey) || transactionKeys.Contains(l.StorageTransactionKey))
            .ToListAsync();

        var plannerLines = lines.Select(l => new PlannerLine(
            l.Key, l.InvoiceType, l.IssueDate, l.TaxDocument,
            QuantityKg(l) - links.Where(x => x.PurchaseInvoiceItemKey == l.Key).Sum(x => x.Quantity))).ToList();
        var plannerTransactions = transactions.Select(t => new PlannerTransaction(
            t.Key, t.TransactionType, t.TransactionDate, t.Code ?? "",
            t.NetWeight - links.Where(x => x.StorageTransactionKey == t.Key).Sum(x => x.Quantity))).ToList();

        var suggested = PurchaseFiscalReconciliationPlanner.Suggest(plannerLines, plannerTransactions)
            .ToDictionary(p => (p.LineKey, p.TransactionKey), p => p.Quantity);

        var result = new List<FiscalReconciliationPairDto>();
        foreach (var line in lines)
        foreach (var tx in transactions.Where(t => R.SenseMatches(line.InvoiceType, t.TransactionType)))
        {
            var current = links.Where(x => x.PurchaseInvoiceItemKey == line.Key && x.StorageTransactionKey == tx.Key).Sum(x => x.Quantity);
            var extra = suggested.GetValueOrDefault((line.Key, tx.Key));
            if (current == 0 && extra == 0) continue;

            result.Add(new FiscalReconciliationPairDto
            {
                PurchaseInvoiceItemKey = line.Key, StorageTransactionKey = tx.Key, TaxDocument = line.TaxDocument,
                TransactionCode = tx.Code, CurrentQuantity = current, Quantity = current + extra,
            });
        }

        return result;
    }

    public async Task SaveAsync(IReadOnlyList<FiscalReconciliationPairDto> pairs, string userName)
    {
        if (pairs.Count == 0)
            throw new DefaultException("Nenhum vínculo para gravar.");

        if (pairs.Any(p => p.Quantity < 0))
            throw new DefaultException("A quantidade do vínculo não pode ser negativa.");

        if (pairs.GroupBy(p => (p.PurchaseInvoiceItemKey, p.StorageTransactionKey)).Any(g => g.Count() > 1))
            throw new DefaultException("O mesmo par NF × romaneio aparece mais de uma vez.");

        var lineKeys = pairs.Select(p => p.PurchaseInvoiceItemKey).Distinct().ToList();
        var transactionKeys = pairs.Select(p => p.StorageTransactionKey).Distinct().ToList();
        var lines = (await LoadLinesAsync(lineKeys)).ToDictionary(l => l.Key);
        var transactions = (await LoadTransactionsAsync(transactionKeys)).ToDictionary(t => t.Key);
        var contractsByTransaction = (await db.Context.PurchaseContractsAllocations.AsNoTracking()
                .Where(a => transactionKeys.Contains(a.StorageTransactionKey))
                .Select(a => new { a.StorageTransactionKey, a.PurchaseContractKey })
                .ToListAsync())
            .ToLookup(a => a.StorageTransactionKey, a => a.PurchaseContractKey);

        foreach (var pair in pairs.Where(p => p.Quantity > 0))
            EnsurePair(lines[pair.PurchaseInvoiceItemKey], transactions[pair.StorageTransactionKey], contractsByTransaction);

        // Teto da NF (D7): o que a linha já tem em OUTROS romaneios + o que este pedido grava. Rastreada: o upsert
        // abaixo altera estas mesmas instâncias.
        var existing = await db.Context.PurchaseFiscalReconciliations
            .Where(l => lineKeys.Contains(l.PurchaseInvoiceItemKey) &&
                        l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .ToListAsync();

        foreach (var line in lines.Values)
        {
            var requested = pairs.Where(p => p.PurchaseInvoiceItemKey == line.Key).ToList();
            var others = existing
                .Where(e => e.PurchaseInvoiceItemKey == line.Key && requested.All(r => r.StorageTransactionKey != e.StorageTransactionKey))
                .Sum(e => e.Quantity);
            var total = others + requested.Sum(r => r.Quantity);
            var limit = QuantityKg(line);
            if (total > limit)
                throw new DefaultException(
                    $"A quantidade vinculada da NF {line.TaxDocument} ({total:N3} KG) passa da quantidade da linha ({limit:N3} KG).");
        }

        foreach (var pair in pairs)
        {
            var link = existing.FirstOrDefault(e =>
                e.PurchaseInvoiceItemKey == pair.PurchaseInvoiceItemKey && e.StorageTransactionKey == pair.StorageTransactionKey);

            if (pair.Quantity == 0)
            {
                if (link != null) db.Context.PurchaseFiscalReconciliations.Remove(link);
                continue;
            }

            if (link != null)
                link.Quantity = pair.Quantity;
            else
                db.Context.PurchaseFiscalReconciliations.Add(new PurchaseFiscalReconciliation
                {
                    PurchaseInvoiceItemKey = pair.PurchaseInvoiceItemKey, StorageTransactionKey = pair.StorageTransactionKey,
                    Quantity = pair.Quantity, CreatedAt = DateTime.Now, CreatedBy = userName,
                });
        }

        await ClearConfirmationAsync(transactionKeys);
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid linkKey)
    {
        var link = await db.Context.PurchaseFiscalReconciliations.FirstOrDefaultAsync(l => l.Key == linkKey)
                   ?? throw new NotFoundException("Vínculo não encontrado.");

        db.Context.PurchaseFiscalReconciliations.Remove(link);
        await ClearConfirmationAsync([link.StorageTransactionKey]);
        await db.SaveChangesAsync();
    }

    public async Task ConfirmAsync(Guid transactionKey, string userName)
    {
        var tx = await TrackedTransactionAsync(transactionKey);
        if (!await PurchaseFiscalReconciliationQuery.ActiveLinks(db.Context).AnyAsync(l => l.StorageTransactionKey == transactionKey))
            throw new DefaultException("Vincule ao menos uma NF antes de marcar o romaneio como conciliado.");

        tx.FiscalReconciledAt = DateTime.Now;
        tx.FiscalReconciledBy = userName;
        await db.SaveChangesAsync();
    }

    public async Task UnconfirmAsync(Guid transactionKey)
    {
        var tx = await TrackedTransactionAsync(transactionKey);
        tx.FiscalReconciledAt = null;
        tx.FiscalReconciledBy = null;
        await db.SaveChangesAsync();
    }

    private sealed record LineData(
        Guid Key, string ItemCode, string? Unit, decimal Quantity, Guid? ContractKey, string? ContractCode,
        PurchaseInvoiceType InvoiceType, InvoiceStatus InvoiceStatus, bool IsCounterNote, bool IsCustodyNote, DateTime? IssueDate,
        string TaxDocument, ItemComplement? Complement);

    private async Task<List<LineData>> LoadLinesAsync(IReadOnlyCollection<Guid> keys)
    {
        var rows = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => keys.Contains(l.Key!.Value))
            .Select(l => new
            {
                Key = l.Key!.Value, l.ItemCode, l.UnitOfMeasureCode, l.Quantity, l.PurchaseContractKey,
                ContractCode = l.PurchaseContract!.Code, l.PurchaseInvoice!.InvoiceType, l.PurchaseInvoice.InvoiceStatus,
                l.PurchaseInvoice.ProducerInvoiceKey, l.PurchaseInvoice.CustodyStorageTransactionKey, l.PurchaseInvoice.IssueDate, l.PurchaseInvoice.TaxDocumentNumber,
                l.PurchaseInvoice.TaxDocumentSeries, l.PurchaseInvoice.InvoiceNumber,
            })
            .ToListAsync();

        if (rows.Count != keys.Count)
            throw new NotFoundException("Linha de documento de entrada não encontrada.");

        var itemCodes = rows.Where(r => r.ItemCode != null).Select(r => r.ItemCode!).Distinct().ToList();
        var complements = await db.Context.ItemComplements.AsNoTracking()
            .Where(c => itemCodes.Contains(c.ItemCode)).ToDictionaryAsync(c => c.ItemCode);

        return rows.Select(r => new LineData(
            r.Key, r.ItemCode ?? "", r.UnitOfMeasureCode, r.Quantity, r.PurchaseContractKey, r.ContractCode,
            r.InvoiceType, r.InvoiceStatus, r.ProducerInvoiceKey != null, r.CustodyStorageTransactionKey != null, r.IssueDate,
            R.TaxDocument(r.TaxDocumentNumber, r.TaxDocumentSeries, r.InvoiceNumber),
            r.ItemCode == null ? null : complements.GetValueOrDefault(r.ItemCode))).ToList();
    }

    private async Task<List<StorageTransaction>> LoadTransactionsAsync(IReadOnlyCollection<Guid> keys)
    {
        var rows = await db.Context.StorageTransactions.AsNoTracking().Where(t => keys.Contains(t.Key)).ToListAsync();
        if (rows.Count != keys.Count)
            throw new NotFoundException("Romaneio não encontrado.");
        return rows;
    }

    private static decimal QuantityKg(LineData line) =>
        R.KgFactor(line.Unit, line.Complement) is { } factor
            ? line.Quantity * factor
            : throw new DefaultException(
                $"Cadastre o fator da unidade {line.Unit} no Complemento do Produto {line.ItemCode} para vincular a NF {line.TaxDocument}.");

    private static void EnsurePair(LineData line, StorageTransaction tx, ILookup<Guid, Guid> contractsByTransaction)
    {
        if (line.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException($"O documento de entrada {line.TaxDocument} está cancelado e não pode ser conciliado.");

        if (line.IsCounterNote)
            throw new DefaultException($"A NF {line.TaxDocument} é contra-nota: vincule a escrituração da NF do produtor.");

        if (line.IsCustodyNote)
            throw new DefaultException($"A NF {line.TaxDocument} é nota de custódia de armazenagem, não NF de venda do fornecedor.");

        if (!R.IsLinkable(tx.TransactionType))
            throw new DefaultException(
                $"O romaneio {tx.Code} não é romaneio de compra (Compra, Dev.Compra, Compl.Qtd., Quebra ou Sobra Descarga).");

        if (tx.TransactionStatus == StorageTransactionsStatus.Cancelled)
            throw new DefaultException($"O romaneio {tx.Code} está cancelado.");

        if (!R.SenseMatches(line.InvoiceType, tx.TransactionType))
            throw new DefaultException(
                "NF de devolução só se vincula a Dev.Compra ou Quebra Descarga; NF normal, a Compra, Compl.Qtd. ou Sobra Descarga " +
                $"(NF {line.TaxDocument} × romaneio {tx.Code}).");

        if (!string.Equals(line.ItemCode, tx.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException(
                $"O produto da NF {line.TaxDocument} ({line.ItemCode}) difere do produto do romaneio {tx.Code} ({tx.ItemCode}).");

        if (line.ContractKey is { } contractKey && !contractsByTransaction[tx.Key].Contains(contractKey))
            throw new DefaultException(
                $"O romaneio {tx.Code} não está alocado no contrato {line.ContractCode} da linha da NF {line.TaxDocument}.");

        _ = QuantityKg(line);
    }

    private async Task ClearConfirmationAsync(IReadOnlyCollection<Guid> transactionKeys)
    {
        var tracked = await db.Context.StorageTransactions
            .Where(t => transactionKeys.Contains(t.Key) && t.FiscalReconciledAt != null).ToListAsync();
        foreach (var tx in tracked)
        {
            tx.FiscalReconciledAt = null;
            tx.FiscalReconciledBy = null;
        }
    }

    private async Task<StorageTransaction> TrackedTransactionAsync(Guid key) =>
        await db.Context.StorageTransactions.FirstOrDefaultAsync(t => t.Key == key)
        ?? throw new NotFoundException("Romaneio não encontrado.");

    private static void EnsureFilter(FiscalReconciliationFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.CardCode) || string.IsNullOrWhiteSpace(filter.ItemCode))
            throw new DefaultException("Informe o fornecedor e o produto.");
    }
}
