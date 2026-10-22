using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>Contextos do Documento de Saída: um por item (610/630) e um do documento (620/635).</summary>
public static class SalesInvoicePostingContext
{
    public const string DocumentType = "SalesInvoice";
    public const string ConfirmEvent = "Confirm";
    public const string UndoEvent = "Undo";

    public static async Task<(PostingContext Lines, PostingContext Document)> BuildAsync(IUnitOfWork db,
        SalesInvoice invoice, IReadOnlyList<SalesInvoiceItem> lines, string evt, DateTime date, bool external = false)
    {
        var itemCodes = lines.Select(l => l.ItemCode).Where(c => c != null).Distinct().ToList();
        var itemAccounts = await db.Context.Items.AsNoTracking()
            .Where(i => itemCodes.Contains(i.ItemCode))
            .ToDictionaryAsync(i => i.ItemCode, i => i.LedgerAccountCode);
        var partnerAccount = await db.Context.BusinessPartners.AsNoTracking()
            .Where(p => p.CardCode == invoice.CardCode).Select(p => p.DefaultLedgerAccountCode).FirstOrDefaultAsync();
        var docNumber = invoice.TaxDocumentNumber ?? invoice.InvoiceNumber ?? string.Empty;

        Dictionary<string, string> Placeholders(string? itemCode) => new()
        {
            ["{DocNumber}"] = docNumber, ["{PartnerName}"] = invoice.CardName ?? string.Empty,
            ["{PartnerCode}"] = invoice.CardCode ?? string.Empty, ["{ItemCode}"] = itemCode ?? string.Empty,
        };

        var lineRows = lines.OrderBy(l => l.ItemCode).ThenBy(l => l.Key).Select(l => new PostingRow(
            new Dictionary<string, string?>
            {
                ["Line.LedgerAccount"] = l.LedgerAccountCode,
                ["Item.LedgerAccount"] = l.ItemCode is null ? null : itemAccounts.GetValueOrDefault(l.ItemCode),
                ["Partner.LedgerAccount"] = partnerAccount,
                ["Document.LedgerAccount"] = invoice.LedgerAccountCode,
            },
            new Dictionary<string, decimal>
            {
                ["Line.Total"] = l.Total,
                ["Line.GrandTotal"] = l.GrandTotal,
                ["Line.NetTotal"] = l.NetTotal,
                ["Line.IcmsValue"] = l.IcmsValue,
                ["Line.PisValue"] = l.PisValue,
                ["Line.CofinsValue"] = l.CofinsValue,
                ["Line.FreightValue"] = l.FreightValue,
                ["Line.DiscountValue"] = l.DiscountValue,
            },
            l.CostCenterCode ?? invoice.CostCenterCode,
            l.AccountingItemCode ?? invoice.AccountingItemCode,
            l.CostCenterCode is null && l.AccountingItemCode is null ? l.DistributionRuleCode ?? invoice.DistributionRuleCode : l.DistributionRuleCode,
            Placeholders(l.ItemCode))).ToList();

        var documentRow = new PostingRow(
            new Dictionary<string, string?>
            {
                ["Partner.LedgerAccount"] = partnerAccount,
                ["Document.LedgerAccount"] = invoice.LedgerAccountCode,
            },
            new Dictionary<string, decimal> { ["Document.GrandTotal"] = lines.Sum(l => l.GrandTotal) },
            invoice.CostCenterCode, invoice.AccountingItemCode, invoice.DistributionRuleCode, Placeholders(null));

        var key = invoice.Key.ToString();
        var branch = invoice.BranchCode ?? string.Empty;
        return (new PostingContext(DocumentType, key, evt, date, branch, invoice.CardCode, lineRows, external),
            new PostingContext(DocumentType, key, evt, date, branch, invoice.CardCode, [documentRow], external));
    }
}
