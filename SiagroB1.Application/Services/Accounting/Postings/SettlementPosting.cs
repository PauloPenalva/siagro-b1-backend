using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>
/// Baixas (520/530) e estornos de baixa (527/531). O ledger de baixas só recebe linhas: positiva é baixa, negativa
/// com <see cref="FinancialSettlement.ReversedSettlementKey"/> é estorno. Quem adiciona linhas chama
/// <see cref="EnqueuePendingAsync"/> antes de gravar; chamar de novo não duplica (o motor ignora evento já lançado).
/// </summary>
public static class SettlementPosting
{
    public const string DocumentType = "FinancialSettlement";
    public const string SettleEvent = "Settle";
    public const string ReverseEvent = "Reverse";

    public static async Task EnqueuePendingAsync(IAccountingPostingService? engine, AppDbContext db, string user)
    {
        if (engine is null) return;

        var pending = db.ChangeTracker.Entries<FinancialSettlement>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        foreach (var settlement in pending)
        {
            // Composição (e o desfazer dela) só liga o provisório ao título da nota: não é pagamento.
            // Devolução de adiantamento é dinheiro voltando: com as fontes da fase 2 sairia como pagamento; fica manual.
            if (settlement.Origin is FinancialSettlementOrigin.InvoiceOffset or FinancialSettlementOrigin.AdvanceRefund) continue;

            var document = await db.FinancialDocuments.FindAsync(settlement.FinancialDocumentKey);
            if (document is null || document.Nature == FinancialDocumentNature.Provisional) continue;

            var receivable = document.Direction == FinancialDirection.Receivable;
            if (settlement.Amount > 0)
            {
                await engine.PostAsync(receivable ? "520" : "530",
                    await ContextAsync(db, settlement, document, settlement.Key, SettleEvent), user);
            }
            else if (settlement.Amount < 0 && settlement.ReversedSettlementKey is { } original)
            {
                await engine.PostReversalAsync(receivable ? "527" : "531",
                    await ContextAsync(db, settlement, document, original, ReverseEvent), SettleEvent, user);
            }
        }
    }

    private static async Task<PostingContext> ContextAsync(AppDbContext db, FinancialSettlement settlement,
        FinancialDocument document, Guid key, string evt)
    {
        var account = settlement.FinancialAccountCode is null
            ? null
            : await db.FinancialAccounts.AsNoTracking().Where(a => a.Code == settlement.FinancialAccountCode)
                .Select(a => new { a.LedgerAccountCode, a.BranchCode }).FirstOrDefaultAsync();
        var partnerAccount = await db.BusinessPartners.AsNoTracking()
            .Where(p => p.CardCode == document.CardCode).Select(p => p.DefaultLedgerAccountCode).FirstOrDefaultAsync();
        var related = settlement.RelatedDocumentKey is null
            ? null
            : await db.FinancialDocuments.AsNoTracking().Where(d => d.Key == settlement.RelatedDocumentKey)
                .Select(d => d.LedgerAccountCode).FirstOrDefaultAsync();

        var row = new PostingRow(
            new Dictionary<string, string?>
            {
                ["FinancialAccount.LedgerAccount"] = account?.LedgerAccountCode,
                ["Partner.LedgerAccount"] = partnerAccount,
                ["Document.LedgerAccount"] = document.LedgerAccountCode,
                ["RelatedDocument.LedgerAccount"] = related,
            },
            new Dictionary<string, decimal>
            {
                ["Settlement.Amount"] = Math.Abs(settlement.Amount),
                ["Settlement.InterestAmount"] = Math.Abs(settlement.InterestAmount),
                ["Settlement.FineAmount"] = Math.Abs(settlement.FineAmount),
                ["Settlement.DiscountAmount"] = Math.Abs(settlement.DiscountAmount),
            },
            document.CostCenterCode, document.AccountingItemCode, document.DistributionRuleCode,
            new Dictionary<string, string>
            {
                ["{DocNumber}"] = document.Code ?? string.Empty, ["{PartnerName}"] = document.CardName ?? string.Empty,
                ["{PartnerCode}"] = document.CardCode ?? string.Empty, ["{Description}"] = settlement.Notes ?? string.Empty,
            });

        var branch = await PostingBranch.ResolveAsync(db, document.BranchCode ?? account?.BranchCode);
        return new PostingContext(DocumentType, key.ToString(), evt, settlement.SettlementDate.Date, branch,
            document.CardCode, [row]);
    }
}
