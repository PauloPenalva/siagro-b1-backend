using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Notas de entrada por período, uma linha por documento. Inclui as próprias (emissão própria,
/// devolução de compra) e as de terceiro (fornecedor, cliente devolvendo).
/// </summary>
public class PurchaseInvoicesByPeriodReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<PurchaseInvoicesByPeriodRowDto>> BuildRowsAsync(PurchaseInvoicesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.PurchaseInvoices
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Items)
            .Where(x => x.IssueDate >= from && x.IssueDate < toExclusive)
            .Where(x => statuses.Contains(x.InvoiceStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(x => x.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(x => x.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(x => x.Items.Any(i => i.ItemCode == request.ItemCode));

        if (request.InvoiceType is { } type)
            query = query.Where(x => x.InvoiceType == type);

        if (request.IssuerType is { } issuer)
            query = query.Where(x => x.IssuerType == issuer);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(x => nfe.Contains(x.NfeStatus));
        }

        var invoices = await query.ToListAsync();

        return invoices
            .OrderBy(x => x.IssueDate)
            .ThenBy(x => x.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(PurchaseInvoicesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = new List<string>();
        if (request.InvoiceType is { } type)
            extra.Add($"Tipo: {DescribeType(type)}");
        if (request.IssuerType is { } issuer)
            extra.Add($"Emissão: {DescribeIssuer(issuer)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Emitente", first?.Issuer, null, first?.Branch, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "PurchaseInvoicesByPeriod.frx", rows, "PurchaseInvoicesByPeriod", "PurchaseInvoicesByPeriod", parameters);
    }

    private static PurchaseInvoicesByPeriodRowDto ToRow(PurchaseInvoice x) => new()
    {
        Branch = InvoiceReportText.BranchName(x.Branch, x.BranchCode),
        InternalNumber = x.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(x.TaxDocumentNumber, x.TaxDocumentSeries),
        IssueDate = InvoiceReportText.Date(x.IssueDate),
        PostingDate = InvoiceReportText.Date(x.PostingDate),
        Issuer = InvoiceReportText.Partner(x.CardCode, x.CardName),
        Type = DescribeType(x.InvoiceType),
        IssuerType = DescribeIssuer(x.IssuerType),
        Status = InvoiceReportText.Status(x.InvoiceStatus),
        NfeStatus = InvoiceReportText.Nfe(x.NfeStatus),
        DeclaredValue = x.TotalDocumentValue,
        ProductsTotal = x.TotalInvoiceItems,
        Freight = x.TotalFreight,
        Discount = x.TotalDiscount,
        Taxes = x.TotalInvoiceTaxes,
        GrandTotal = x.GrandTotal,
    };

    internal static string DescribeType(PurchaseInvoiceType type) =>
        type == PurchaseInvoiceType.Return ? "Devolução" : "Normal";

    internal static string DescribeIssuer(DocumentIssuerType issuer) =>
        issuer == DocumentIssuerType.Own ? "Própria" : "Terceiro";
}
