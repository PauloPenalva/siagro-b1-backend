using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Itens dos documentos de saída, agrupados por produto E unidade de medida — o subtotal de
/// quantidade nunca soma KG com TN. Produto, UM, CFOP, natureza e tributos vêm do snapshot da
/// linha; contrato por LEFT JOIN (FK opcional).
/// </summary>
public class SalesInvoiceItemsReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<InvoiceItemRowDto>> BuildRowsAsync(SalesInvoiceItemsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Include(i => i.SalesInvoice)
            .Include(i => i.SalesContract)
            .Where(i => i.SalesInvoice != null)
            .Where(i => i.SalesInvoice!.InvoiceDate >= from && i.SalesInvoice.InvoiceDate < toExclusive)
            .Where(i => statuses.Contains(i.SalesInvoice!.InvoiceStatus ?? InvoiceStatus.Pending));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.SalesInvoice!.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.SalesInvoice!.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        if (request.InvoiceType is { } type)
            query = query.Where(i => i.SalesInvoice!.InvoiceType == type);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(i => i.SalesContract != null && i.SalesContract.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.Cfop))
            query = query.Where(i => i.Cfop == request.Cfop);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(i => nfe.Contains(i.SalesInvoice!.NfeStatus));
        }

        var items = await query.ToListAsync();

        return items
            .OrderBy(i => GroupOf(i), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.SalesInvoice!.InvoiceDate)
            .ThenBy(i => i.SalesInvoice!.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesInvoiceItemsRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = new List<string>();
        if (request.InvoiceType is { } type)
            extra.Add($"Tipo: {(type == SalesInvoiceType.Return ? "Devolução" : "Normal")}");
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.Cfop))
            extra.Add($"CFOP: {request.Cfop}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Cliente", first?.Partner, ProductName(first), null, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "SalesInvoiceItems.frx", rows, "InvoiceItems", "InvoiceItems", parameters);
    }

    private static string GroupOf(SalesInvoiceItem i) =>
        $"{InvoiceReportText.Product(i.ItemCode, i.ItemName)} - {i.UnitOfMeasureCode}";

    private static InvoiceItemRowDto ToRow(SalesInvoiceItem i) => new()
    {
        Group = GroupOf(i),
        IssueDate = InvoiceReportText.Date(i.SalesInvoice!.InvoiceDate),
        InternalNumber = i.SalesInvoice.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(i.SalesInvoice.TaxDocumentNumber, i.SalesInvoice.TaxDocumentSeries),
        Partner = InvoiceReportText.Partner(i.SalesInvoice.CardCode, i.SalesInvoice.CardName),
        Cfop = i.Cfop ?? "",
        Usage = i.UsageName ?? "",
        Quantity = i.Quantity,
        UnitOfMeasure = i.UnitOfMeasureCode,
        UnitPrice = i.UnitPrice,
        Total = i.Total,
        Icms = i.IcmsValue,
        Pis = i.PisValue,
        Cofins = i.CofinsValue,
        IbsCbs = i.TotalIbsCbs,
        GrandTotal = i.GrandTotal,
        Contract = i.SalesContract?.Code ?? "",
    };

    /// <summary>Nome do produto filtrado, sem o sufixo " - UM" do grupo.</summary>
    private static string? ProductName(InvoiceItemRowDto? row)
    {
        if (row is null)
            return null;

        var cut = row.Group.LastIndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? row.Group[..cut] : row.Group;
    }
}
