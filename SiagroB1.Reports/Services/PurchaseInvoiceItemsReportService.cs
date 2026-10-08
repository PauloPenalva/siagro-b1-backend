using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Itens dos documentos de entrada, agrupados por produto e UM. Entrada importada de XML pode
/// não ter produto vinculado (ItemCode nulo): agrupa pelo nome do XML ou em
/// "Sem produto vinculado" — nunca some do relatório.
/// </summary>
public class PurchaseInvoiceItemsReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<InvoiceItemRowDto>> BuildRowsAsync(PurchaseInvoiceItemsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Include(i => i.PurchaseContract)
            .Where(i => i.PurchaseInvoice != null)
            .Where(i => i.PurchaseInvoice!.IssueDate >= from && i.PurchaseInvoice.IssueDate < toExclusive)
            .Where(i => statuses.Contains(i.PurchaseInvoice!.InvoiceStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.PurchaseInvoice!.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.PurchaseInvoice!.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        if (request.InvoiceType is { } type)
            query = query.Where(i => i.PurchaseInvoice!.InvoiceType == type);

        if (request.IssuerType is { } issuer)
            query = query.Where(i => i.PurchaseInvoice!.IssuerType == issuer);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(i => i.PurchaseContract != null && i.PurchaseContract.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.Cfop))
            query = query.Where(i => i.Cfop == request.Cfop);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(i => nfe.Contains(i.PurchaseInvoice!.NfeStatus));
        }

        var items = await query.ToListAsync();

        return items
            .OrderBy(i => GroupOf(i), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.PurchaseInvoice!.IssueDate)
            .ThenBy(i => i.PurchaseInvoice!.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(PurchaseInvoiceItemsRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = new List<string>();
        if (request.InvoiceType is { } type)
            extra.Add($"Tipo: {PurchaseInvoicesByPeriodReportService.DescribeType(type)}");
        if (request.IssuerType is { } issuer)
            extra.Add($"Emitida por: {PurchaseInvoicesByPeriodReportService.DescribeIssuer(issuer)}");
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.Cfop))
            extra.Add($"CFOP: {request.Cfop}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Emitente", first?.Partner, ProductName(first), null, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "PurchaseInvoiceItems.frx", rows, "InvoiceItems", "InvoiceItems", parameters);
    }

    /// <summary>Nome do produto filtrado, sem o sufixo " - UM" do grupo.</summary>
    private static string? ProductName(InvoiceItemRowDto? row)
    {
        if (row is null)
            return null;

        var cut = row.Group.LastIndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? row.Group[..cut] : row.Group;
    }

    private static string GroupOf(PurchaseInvoiceItem i)
    {
        var product = InvoiceReportText.Product(i.ItemCode, i.ItemName);
        return string.IsNullOrWhiteSpace(i.UnitOfMeasureCode) ? product : $"{product} - {i.UnitOfMeasureCode}";
    }

    private static InvoiceItemRowDto ToRow(PurchaseInvoiceItem i) => new()
    {
        Group = GroupOf(i),
        IssueDate = InvoiceReportText.Date(i.PurchaseInvoice!.IssueDate),
        InternalNumber = i.PurchaseInvoice.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(i.PurchaseInvoice.TaxDocumentNumber, i.PurchaseInvoice.TaxDocumentSeries),
        Partner = InvoiceReportText.Partner(i.PurchaseInvoice.CardCode, i.PurchaseInvoice.CardName),
        Cfop = i.Cfop ?? "",
        Usage = i.UsageName ?? "",
        Quantity = i.Quantity,
        UnitOfMeasure = i.UnitOfMeasureCode ?? "",
        UnitPrice = i.UnitPrice,
        Total = i.Total,
        Icms = i.IcmsValue,
        Pis = i.PisValue,
        Cofins = i.CofinsValue,
        IbsCbs = i.TotalIbsCbs,
        GrandTotal = i.GrandTotal,
        Contract = i.PurchaseContract?.Code ?? "",
    };
}
