using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Leitura da Conciliação Fiscal de Compras (spec 2026-10-13 §5). Mora no Infra porque a tela (Web) e o relatório
/// (Reports, que não referencia a Application) precisam dos MESMOS números. Os IQueryable são públicos para o teste de
/// tradução SQL; a composição (contratos, NFs, status) é feita em memória sobre poucas linhas.
/// </summary>
public static class PurchaseFiscalReconciliationQuery
{
    /// <summary>Vínculo que conta: romaneio não cancelado e NF não cancelada (defesa em profundidade do §4.2).</summary>
    public static IQueryable<PurchaseFiscalReconciliation> ActiveLinks(AppDbContext ctx) =>
        ctx.PurchaseFiscalReconciliations.AsNoTracking()
            .Where(l => l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        l.PurchaseInvoiceItem!.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled);

    public static IQueryable<StorageTransaction> TransactionsBase(AppDbContext ctx, FiscalReconciliationFilter f)
    {
        var query = ctx.StorageTransactions.AsNoTracking()
            .Where(t => R.LinkableTypes.Contains(t.TransactionType) &&
                        t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        t.CardCode == f.CardCode && t.ItemCode == f.ItemCode);

        if (f.DateFrom is { } from)
        {
            var start = from.Date;
            query = query.Where(t => t.TransactionDate >= start);
        }

        if (f.DateTo is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(t => t.TransactionDate < end);
        }

        if (!string.IsNullOrWhiteSpace(f.ContractCode))
        {
            var code = f.ContractCode.Trim();
            query = query.Where(t => ctx.PurchaseContractsAllocations
                .Any(a => a.StorageTransactionKey == t.Key && a.PurchaseContract!.Code == code));
        }

        return query;
    }

    public sealed class LinkRow
    {
        public Guid Key { get; set; }
        public Guid PurchaseInvoiceItemKey { get; set; }
        public Guid PurchaseInvoiceKey { get; set; }
        public Guid StorageTransactionKey { get; set; }
        public string? TaxDocumentNumber { get; set; }
        public string? TaxDocumentSeries { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? CardCode { get; set; }
        public string? CardName { get; set; }
        public DateTime? IssueDate { get; set; }
        public decimal Quantity { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }

    public static IQueryable<LinkRow> LinksQuery(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ActiveLinks(ctx)
            .Where(l => transactionKeys.Contains(l.StorageTransactionKey))
            .Select(l => new LinkRow
            {
                Key = l.Key,
                PurchaseInvoiceItemKey = l.PurchaseInvoiceItemKey,
                PurchaseInvoiceKey = l.PurchaseInvoiceItem!.PurchaseInvoiceKey!.Value,
                StorageTransactionKey = l.StorageTransactionKey,
                TaxDocumentNumber = l.PurchaseInvoiceItem.PurchaseInvoice!.TaxDocumentNumber,
                TaxDocumentSeries = l.PurchaseInvoiceItem.PurchaseInvoice.TaxDocumentSeries,
                InvoiceNumber = l.PurchaseInvoiceItem.PurchaseInvoice.InvoiceNumber,
                CardCode = l.PurchaseInvoiceItem.PurchaseInvoice.CardCode,
                CardName = l.PurchaseInvoiceItem.PurchaseInvoice.CardName,
                IssueDate = l.PurchaseInvoiceItem.PurchaseInvoice.IssueDate,
                Quantity = l.Quantity,
                CreatedAt = l.CreatedAt,
                CreatedBy = l.CreatedBy,
            });

    public sealed class ContractRef
    {
        public Guid StorageTransactionKey { get; set; }
        public string? Code { get; set; }
    }

    public static IQueryable<ContractRef> ContractsQuery(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ctx.PurchaseContractsAllocations.AsNoTracking()
            .Where(a => transactionKeys.Contains(a.StorageTransactionKey))
            .Select(a => new ContractRef { StorageTransactionKey = a.StorageTransactionKey, Code = a.PurchaseContract!.Code });

    public static async Task<List<FiscalReconciliationLinkDto>> LoadLinksAsync(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys)
    {
        var rows = await LinksQuery(ctx, transactionKeys).ToListAsync();

        // Spec 2026-10-13 (títulos firmes): o firme de cada vínculo — o ativo, ou o último cancelado — e a situação.
        var linkKeys = rows.Select(r => r.Key).ToList();
        var firms = await ctx.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
                        d.OriginKey != null && linkKeys.Contains(d.OriginKey.Value))
            .Select(d => new { d.Key, d.Code, OriginKey = d.OriginKey!.Value, d.Status, d.CreatedAt })
            .ToListAsync();
        var firmKeys = firms.Select(f => f.Key).ToList();
        var composed = (await ctx.FinancialDocumentCompositions.AsNoTracking()
                .Where(c => firmKeys.Contains(c.ComposedDocumentKey))
                .Select(c => c.ComposedDocumentKey)
                .ToListAsync())
            .ToHashSet();
        var firmByLink = firms
            .GroupBy(f => f.OriginKey)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(f => f.Status == FinancialDocumentStatus.Canceled ? 1 : 0)
                .ThenByDescending(f => f.CreatedAt)
                .First());

        return rows
            .Select(r =>
            {
                var firm = firmByLink.GetValueOrDefault(r.Key);
                return new FiscalReconciliationLinkDto
                {
                    Key = r.Key, PurchaseInvoiceItemKey = r.PurchaseInvoiceItemKey, PurchaseInvoiceKey = r.PurchaseInvoiceKey,
                    StorageTransactionKey = r.StorageTransactionKey,
                    TaxDocument = R.TaxDocument(r.TaxDocumentNumber, r.TaxDocumentSeries, r.InvoiceNumber),
                    CardCode = r.CardCode, CardName = r.CardName, IssueDate = r.IssueDate, Quantity = r.Quantity,
                    CreatedAt = r.CreatedAt, CreatedBy = r.CreatedBy,
                    FirmDocumentKey = firm?.Key,
                    FirmDocumentCode = firm?.Code,
                    FirmSituation = firm is null ? null : PurchaseFirmPayableRules.SituationName(firm.Status, composed.Contains(firm.Key)),
                };
            })
            .OrderBy(l => l.IssueDate).ThenBy(l => l.TaxDocument, StringComparer.Ordinal)
            .ToList();
    }

    public static async Task<List<FiscalReconciliationTransactionDto>> LoadTransactionsAsync(AppDbContext ctx, FiscalReconciliationFilter f)
    {
        var transactions = await TransactionsBase(ctx, f)
            .OrderBy(t => t.TransactionDate).ThenBy(t => t.Code)
            .Select(t => new
            {
                t.Key, t.Code, t.TransactionDate, t.TransactionType, t.CardCode, t.CardName, t.ItemCode, t.TruckCode,
                t.UnitOfMeasureCode, t.NetWeight, t.FiscalReconciledAt, t.FiscalReconciledBy,
            })
            .ToListAsync();

        var keys = transactions.Select(t => t.Key).ToList();
        var links = (await LoadLinksAsync(ctx, keys)).ToLookup(l => l.StorageTransactionKey);
        var contracts = (await ContractsQuery(ctx, keys).ToListAsync()).ToLookup(c => c.StorageTransactionKey);

        return transactions.Select(t =>
        {
            var own = links[t.Key].ToList();
            var fiscal = own.Sum(l => l.Quantity);
            var status = R.Status(t.NetWeight, fiscal, own.Count > 0, t.FiscalReconciledAt != null);
            return new FiscalReconciliationTransactionDto
            {
                Key = t.Key, Code = t.Code, TransactionDate = t.TransactionDate, TransactionType = t.TransactionType,
                TransactionTypeName = R.TypeName(t.TransactionType), CardCode = t.CardCode, CardName = t.CardName,
                ItemCode = t.ItemCode, TruckCode = t.TruckCode, UnitOfMeasureCode = t.UnitOfMeasureCode,
                ContractCodes = contracts[t.Key].Select(c => c.Code ?? "").Distinct().Order(StringComparer.Ordinal).ToList(),
                PhysicalQuantity = t.NetWeight, FiscalQuantity = fiscal, Status = status, StatusName = R.StatusName(status),
                FiscalReconciledAt = t.FiscalReconciledAt, FiscalReconciledBy = t.FiscalReconciledBy, Links = own,
            };
        }).ToList();
    }

    /// <summary>Fornecedor + parceiros com o mesmo CPF/raiz de CNPJ (D6). Sem documento: só o próprio fornecedor.</summary>
    public static async Task<HashSet<string>> OwnerCardCodesAsync(AppDbContext ctx, string cardCode)
    {
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cardCode };
        var taxId = await ctx.BusinessPartners.AsNoTracking()
            .Where(bp => bp.CardCode == cardCode).Select(bp => bp.TaxId).FirstOrDefaultAsync();
        var root = R.OwnerRoot(taxId);
        if (root == null)
            return owners;

        // O TaxId pode estar mascarado no cadastro: candidatos pelos 2 primeiros dígitos (contíguos tanto em
        // "123.456.789-01" quanto em "12.345.678/0001-90"), decisão em memória.
        var prefix = root[..2];
        var candidates = await ctx.BusinessPartners.AsNoTracking()
            .Where(bp => bp.TaxId != null && bp.TaxId.Contains(prefix))
            .Select(bp => new { bp.CardCode, bp.TaxId })
            .ToListAsync();

        foreach (var c in candidates.Where(c => R.SameOwner(c.TaxId, taxId)))
            owners.Add(c.CardCode);

        return owners;
    }

    public sealed class InvoiceLineRow
    {
        public Guid ItemKey { get; set; }
        public Guid InvoiceKey { get; set; }
        public PurchaseInvoiceType InvoiceType { get; set; }
        public string? TaxDocumentNumber { get; set; }
        public string? TaxDocumentSeries { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime? IssueDate { get; set; }
        public string CardCode { get; set; } = "";
        public string? CardName { get; set; }
        public string? UsageName { get; set; }
        public string? ContractCode { get; set; }
        public string? UnitOfMeasureCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal Linked { get; set; }
    }

    public static IQueryable<InvoiceLineRow> InvoiceLinesBase(AppDbContext ctx, FiscalReconciliationFilter f, IReadOnlyCollection<string> issuers)
    {
        var query = ctx.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.ItemCode == f.ItemCode &&
                        l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled &&
                        l.PurchaseInvoice.ProducerInvoiceKey == null &&      // contra-nota: vincula-se a escrituração
                        l.PurchaseInvoice.CustodyStorageTransactionKey == null && // nota de custódia não é NF de venda
                        l.PurchaseInvoice.InvoiceType != PurchaseInvoiceType.ValueComplement && // D11: título nasce na confirmação
                        l.PurchaseInvoice.InvoiceType != PurchaseInvoiceType.IcmsComplement && // spec 2026-10-17: não gera título nem tem quantidade
                        // emissão própria só entra Confirmada: o vínculo gera título financeiro e a NF-e pode nunca existir
                        (l.PurchaseInvoice.IssuerType != DocumentIssuerType.Own || l.PurchaseInvoice.InvoiceStatus == InvoiceStatus.Confirmed) &&
                        issuers.Contains(l.PurchaseInvoice.CardCode));

        if (f.DateFrom is { } from)
        {
            var start = from.Date;
            query = query.Where(l => l.PurchaseInvoice!.IssueDate >= start);
        }

        if (f.DateTo is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(l => l.PurchaseInvoice!.IssueDate < end);
        }

        if (!string.IsNullOrWhiteSpace(f.ContractCode))
        {
            var code = f.ContractCode.Trim();
            query = query.Where(l => l.PurchaseContractKey == null || l.PurchaseContract!.Code == code);
        }

        return query.Select(l => new InvoiceLineRow
        {
            ItemKey = l.Key!.Value,
            InvoiceKey = l.PurchaseInvoiceKey!.Value,
            InvoiceType = l.PurchaseInvoice!.InvoiceType,
            TaxDocumentNumber = l.PurchaseInvoice.TaxDocumentNumber,
            TaxDocumentSeries = l.PurchaseInvoice.TaxDocumentSeries,
            InvoiceNumber = l.PurchaseInvoice.InvoiceNumber,
            IssueDate = l.PurchaseInvoice.IssueDate,
            CardCode = l.PurchaseInvoice.CardCode,
            CardName = l.PurchaseInvoice.CardName,
            UsageName = l.UsageName,
            ContractCode = l.PurchaseContract!.Code,
            UnitOfMeasureCode = l.UnitOfMeasureCode,
            Quantity = l.Quantity,
            Linked = ctx.PurchaseFiscalReconciliations
                .Where(r => r.PurchaseInvoiceItemKey == l.Key &&
                            r.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled)
                .Sum(r => (decimal?)r.Quantity) ?? 0m,
        });
    }

    public static async Task<List<FiscalReconciliationInvoiceLineDto>> LoadInvoiceLinesAsync(AppDbContext ctx, FiscalReconciliationFilter f)
    {
        var owners = await OwnerCardCodesAsync(ctx, f.CardCode);
        IReadOnlyCollection<string> issuers = string.IsNullOrWhiteSpace(f.IssuerCardCode) ? owners : [f.IssuerCardCode.Trim()];
        var complement = await ctx.ItemComplements.AsNoTracking().FirstOrDefaultAsync(c => c.ItemCode == f.ItemCode);

        var rows = await InvoiceLinesBase(ctx, f, issuers.ToList()).ToListAsync();

        return rows
            .Select(r =>
            {
                var factor = R.KgFactor(r.UnitOfMeasureCode, complement);
                return new FiscalReconciliationInvoiceLineDto
                {
                    PurchaseInvoiceItemKey = r.ItemKey, PurchaseInvoiceKey = r.InvoiceKey, InvoiceType = r.InvoiceType,
                    TaxDocument = R.TaxDocument(r.TaxDocumentNumber, r.TaxDocumentSeries, r.InvoiceNumber),
                    IssueDate = r.IssueDate, CardCode = r.CardCode, CardName = r.CardName,
                    IssuerMatchesSupplier = owners.Contains(r.CardCode), UsageName = r.UsageName,
                    ContractCode = r.ContractCode, UnitOfMeasureCode = r.UnitOfMeasureCode, Quantity = r.Quantity,
                    QuantityKg = factor * r.Quantity, LinkedQuantity = r.Linked,
                };
            })
            .Where(l => f.IncludeFullyLinked || l.QuantityKg == null || l.AvailableQuantity > 0)
            .OrderBy(l => l.IssueDate).ThenBy(l => l.TaxDocument, StringComparer.Ordinal)
            .ToList();
    }
}
