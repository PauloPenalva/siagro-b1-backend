using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>Contextos do Documento de Entrada: um por item (650/655) e um do documento (660/665).</summary>
public static class PurchaseInvoicePostingContext
{
    public const string DocumentType = "PurchaseInvoice";
    public const string ConfirmEvent = "Confirm";
    public const string UndoEvent = "Undo";

    public static async Task<(PostingContext Lines, PostingContext Document)> BuildAsync(IUnitOfWork db,
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, string evt, DateTime date, bool external = false)
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
                // O mesmo valor que forma a camada PEPS do estoque fiscal (FiscalStockLedgerLoader).
                ["Line.FiscalStockCost"] = l.MovesFiscalInventory ? l.GrandTotal - l.IcmsValue : 0m,
                ["Line.IcmsValue"] = l.IcmsValue,
                ["Line.PisValue"] = l.PisValue,
                ["Line.CofinsValue"] = l.CofinsValue,
                ["Line.FunruralValue"] = l.FunruralValue,
                ["Line.FreightValue"] = l.FreightValue,
                ["Line.DiscountValue"] = l.DiscountValue,
                ["Line.OtherExpensesValue"] = l.OtherExpensesValue,
            },
            l.CostCenterCode ?? invoice.CostCenterCode,
            l.AccountingItemCode ?? invoice.AccountingItemCode,
            l.CostCenterCode is null && l.AccountingItemCode is null ? l.DistributionRuleCode ?? invoice.DistributionRuleCode : l.DistributionRuleCode,
            Placeholders(l.ItemCode))).ToList();

        var grandTotal = lines.Sum(l => l.GrandTotal);
        var funrural = lines.Sum(l => l.FunruralValue);
        var documentRow = new PostingRow(
            new Dictionary<string, string?>
            {
                ["Partner.LedgerAccount"] = partnerAccount,
                ["Document.LedgerAccount"] = invoice.LedgerAccountCode,
            },
            new Dictionary<string, decimal>
            {
                ["Document.GrandTotal"] = grandTotal,
                ["Document.NetPayable"] = grandTotal - funrural - invoice.TotalWithheld,
                ["Document.TotalFunrural"] = funrural,
                ["Document.WithheldIss"] = invoice.WithheldIss,
                ["Document.WithheldIrrf"] = invoice.WithheldIrrf,
                ["Document.WithheldInss"] = invoice.WithheldInss,
                ["Document.WithheldPis"] = invoice.WithheldPis,
                ["Document.WithheldCofins"] = invoice.WithheldCofins,
                ["Document.WithheldCsll"] = invoice.WithheldCsll,
                ["Document.CteIcmsValue"] = invoice.CteIcmsValue ?? 0m,
            },
            invoice.CostCenterCode, invoice.AccountingItemCode, invoice.DistributionRuleCode, Placeholders(null));

        var key = invoice.Key.ToString();
        var branch = invoice.BranchCode ?? string.Empty;
        return (new PostingContext(DocumentType, key, evt, date, branch, invoice.CardCode, lineRows, external),
            new PostingContext(DocumentType, key, evt, date, branch, invoice.CardCode, [documentRow], external));
    }
}
