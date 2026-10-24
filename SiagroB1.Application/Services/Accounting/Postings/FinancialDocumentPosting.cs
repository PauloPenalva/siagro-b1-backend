using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>Filial do lançamento quando o registro de origem não tem uma.</summary>
public static class PostingBranch
{
    /// <summary>A filial informada; vazia, a de menor código cadastrada (decisão da spec §9).</summary>
    public static async Task<string> ResolveAsync(AppDbContext db, string? branchCode)
    {
        if (!string.IsNullOrWhiteSpace(branchCode)) return branchCode.Trim();
        return await db.Branchs.AsNoTracking().OrderBy(b => b.Code).Select(b => b.Code).FirstOrDefaultAsync()
               ?? string.Empty;
    }
}

/// <summary>
/// Inclusão (500/510) e cancelamento (505/515) de título. Só os títulos firmes sem documento fiscal de origem
/// contabilizam: os da nota já estão no 650/660 ou no 610/620 (spec §9).
/// </summary>
public static class FinancialDocumentPosting
{
    public const string DocumentType = "FinancialDocument";
    public const string CreateEvent = "Create";
    public const string CancelEvent = "Cancel";

    private static readonly FinancialDocumentOrigin[] InvoiceOrigins =
    [
        FinancialDocumentOrigin.PurchaseInvoice, FinancialDocumentOrigin.PurchaseFiscalReconciliation,
        FinancialDocumentOrigin.OtherPurchaseInvoice, FinancialDocumentOrigin.SalesInvoice,
        FinancialDocumentOrigin.SalesInvoiceNoContract,
        // Título da fatura de frete substitui os dos CT-e (baixados por aglutinação): a receita já está neles.
        FinancialDocumentOrigin.FreightInvoice,
    ];

    public static bool IsPostable(FinancialDocument document) =>
        document.Nature != FinancialDocumentNature.Provisional && !InvoiceOrigins.Contains(document.OriginType);

    public static async Task CreateAsync(IAccountingPostingService? engine, AppDbContext db, FinancialDocument document,
        string user)
    {
        if (engine is null || !IsPostable(document)) return;
        var code = document.Direction == FinancialDirection.Receivable ? "500" : "510";
        await engine.PostAsync(code, await BuildContextAsync(db, document, CreateEvent, document.DocumentDate), user);
    }

    public static async Task CancelAsync(IAccountingPostingService? engine, AppDbContext db, FinancialDocument document,
        string user)
    {
        if (engine is null || !IsPostable(document)) return;
        var code = document.Direction == FinancialDirection.Receivable ? "505" : "515";
        await engine.PostReversalAsync(code, await BuildContextAsync(db, document, CancelEvent, DateTime.Today), CreateEvent, user);
    }

    public static async Task<PostingContext> BuildContextAsync(AppDbContext db, FinancialDocument document, string evt, DateTime date)
    {
        var partnerAccount = await db.BusinessPartners.AsNoTracking()
            .Where(p => p.CardCode == document.CardCode).Select(p => p.DefaultLedgerAccountCode).FirstOrDefaultAsync();
        var row = new PostingRow(
            new Dictionary<string, string?>
            {
                ["Partner.LedgerAccount"] = partnerAccount,
                ["Document.LedgerAccount"] = document.LedgerAccountCode,
            },
            new Dictionary<string, decimal> { ["Document.NetAmount"] = document.NetAmount },
            document.CostCenterCode, document.AccountingItemCode, document.DistributionRuleCode,
            new Dictionary<string, string>
            {
                ["{DocNumber}"] = document.Code ?? string.Empty, ["{PartnerName}"] = document.CardName ?? string.Empty,
                ["{PartnerCode}"] = document.CardCode ?? string.Empty, ["{Description}"] = document.Comments ?? string.Empty,
            });
        return new PostingContext(DocumentType, document.Key.ToString(), evt, date.Date,
            await PostingBranch.ResolveAsync(db, document.BranchCode), document.CardCode, [row]);
    }
}
