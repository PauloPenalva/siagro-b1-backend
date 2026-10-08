using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Notas de saída por período, uma linha por documento. Substitui o antigo "Documentos de
/// Saída" (SQL embutido no .frx, sem filtro). Os totais não têm coluna no banco: são as
/// propriedades [NotMapped] do domínio, somadas das linhas — por isso o Include de Items.
/// </summary>
public class SalesInvoicesByPeriodReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<SalesInvoicesByPeriodRowDto>> BuildRowsAsync(SalesInvoicesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        // Branch é tabela local com FK opcional -> LEFT JOIN, seguro em SAPB1.
        var query = db.Context.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Items)
            .Where(x => x.InvoiceDate >= from && x.InvoiceDate < toExclusive)
            .Where(x => statuses.Contains(x.InvoiceStatus ?? InvoiceStatus.Pending));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(x => x.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(x => x.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(x => x.Items.Any(i => i.ItemCode == request.ItemCode));

        if (request.InvoiceType is { } type)
            query = query.Where(x => x.InvoiceType == type);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(x => nfe.Contains(x.NfeStatus));
        }

        var invoices = await query.ToListAsync();

        return invoices
            .OrderBy(x => x.InvoiceDate)
            .ThenBy(x => x.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesInvoicesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;

        var extra = request.InvoiceType is { } type ? new[] { $"Tipo: {DescribeType(type)}" } : [];
        var first = rows.Count > 0 ? rows[0] : null;

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Cliente", first?.Customer, null, first?.Branch, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "SalesInvoicesByPeriod.frx", rows, "SalesInvoicesByPeriod", "SalesInvoicesByPeriod", parameters);
    }

    private static SalesInvoicesByPeriodRowDto ToRow(SalesInvoice x) => new()
    {
        Branch = InvoiceReportText.BranchName(x.Branch, x.BranchCode),
        InternalNumber = x.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(x.TaxDocumentNumber, x.TaxDocumentSeries),
        IssueDate = InvoiceReportText.Date(x.InvoiceDate),
        Customer = InvoiceReportText.Partner(x.CardCode, x.CardName),
        Type = DescribeType(x.InvoiceType),
        Status = InvoiceReportText.Status(x.InvoiceStatus),
        NfeStatus = InvoiceReportText.Nfe(x.NfeStatus),
        NetWeight = x.NetWeight,
        ProductsTotal = x.TotalInvoiceItems,
        Freight = x.TotalFreight,
        Discount = x.TotalDiscount,
        Taxes = x.TotalInvoiceTaxes,
        IbsCbs = x.TotalInvoiceIbsCbs,
        GrandTotal = x.GrandTotal,
    };

    private static string DescribeType(SalesInvoiceType type) =>
        type == SalesInvoiceType.Return ? "Devolução" : "Normal";
}
