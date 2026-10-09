using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Devoluções de venda, por item devolvido, juntando as duas formas que existem no sistema:
/// <list type="bullet">
/// <item>Própria: SalesInvoice tipo Return; a original vem de SalesInvoiceItemOriginKey.</item>
/// <item>Do cliente: PurchaseInvoice tipo Return emitida por TERCEIRO; a original vem de
/// PurchaseInvoiceItem.SalesInvoiceItemKey (vínculo manual, pode faltar). Devolução de compra
/// (emissão própria) não entra.</item>
/// </list>
/// Só leitura: a devolução do cliente não move saldo, o relatório também não.
/// </summary>
public class SalesReturnsReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<SalesReturnRowDto>> BuildRowsAsync(SalesReturnsRequest request)
    {
        var rows = new List<SalesReturnRowDto>();

        if (request.Source is null or SalesReturnSource.Own)
            rows.AddRange(await OwnAsync(request));

        if (request.Source is null or SalesReturnSource.Customer)
            rows.AddRange(await CustomerAsync(request));

        return rows
            .OrderBy(r => r.Customer, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.SortDate)
            .ThenBy(r => r.InternalNumber, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesReturnsRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = request.Source switch
        {
            SalesReturnSource.Own => new[] { "Origem: Própria" },
            SalesReturnSource.Customer => ["Origem: Cliente"],
            _ => [],
        };

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Cliente", first?.Customer, first?.Product, null, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "SalesReturns.frx", rows, "SalesReturns", "SalesReturns", parameters);
    }

    private async Task<List<SalesReturnRowDto>> OwnAsync(SalesReturnsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Include(i => i.SalesInvoice)
            .Include(i => i.SalesInvoiceItemOrigin).ThenInclude(o => o!.SalesInvoice)
            .Where(i => i.SalesInvoice != null && i.SalesInvoice.InvoiceType == SalesInvoiceType.Return)
            .Where(i => i.SalesInvoice!.InvoiceDate >= from && i.SalesInvoice.InvoiceDate < toExclusive)
            .Where(i => statuses.Contains(i.SalesInvoice!.InvoiceStatus ?? InvoiceStatus.Pending));

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(i => nfe.Contains(i.SalesInvoice!.NfeStatus));
        }

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.SalesInvoice!.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.SalesInvoice!.CardCode == request.CardCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        var items = await query.ToListAsync();

        return items.Select(i => new SalesReturnRowDto
        {
            Customer = InvoiceReportText.Partner(i.SalesInvoice!.CardCode, i.SalesInvoice.CardName),
            Date = InvoiceReportText.Date(i.SalesInvoice.InvoiceDate),
            SortDate = i.SalesInvoice.InvoiceDate ?? DateTime.MinValue,
            Source = "Própria",
            InternalNumber = i.SalesInvoice.InvoiceNumber ?? "",
            DocumentNumber = InvoiceReportText.DocumentNumber(i.SalesInvoice.TaxDocumentNumber, i.SalesInvoice.TaxDocumentSeries),
            Origin = DescribeOrigin(i.SalesInvoiceItemOrigin?.SalesInvoice),
            Product = InvoiceReportText.Product(i.ItemCode, i.ItemName),
            Quantity = i.Quantity,
            UnitOfMeasure = i.UnitOfMeasureCode,
            Value = i.GrandTotal,
            Status = InvoiceReportText.Status(i.SalesInvoice.InvoiceStatus),
        }).ToList();
    }

    private async Task<List<SalesReturnRowDto>> CustomerAsync(SalesReturnsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Include(i => i.SalesInvoiceItem).ThenInclude(o => o!.SalesInvoice)
            .Where(i => i.PurchaseInvoice != null
                        && i.PurchaseInvoice.InvoiceType == PurchaseInvoiceType.Return
                        && i.PurchaseInvoice.IssuerType == DocumentIssuerType.ThirdParty)
            .Where(i => i.PurchaseInvoice!.IssueDate >= from && i.PurchaseInvoice.IssueDate < toExclusive)
            .Where(i => statuses.Contains(i.PurchaseInvoice!.InvoiceStatus));

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(i => nfe.Contains(i.PurchaseInvoice!.NfeStatus));
        }

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.PurchaseInvoice!.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.PurchaseInvoice!.CardCode == request.CardCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        var items = await query.ToListAsync();

        return items.Select(i => new SalesReturnRowDto
        {
            Customer = InvoiceReportText.Partner(i.PurchaseInvoice!.CardCode, i.PurchaseInvoice.CardName),
            Date = InvoiceReportText.Date(i.PurchaseInvoice.IssueDate),
            SortDate = i.PurchaseInvoice.IssueDate ?? DateTime.MinValue,
            Source = "Cliente",
            InternalNumber = i.PurchaseInvoice.InvoiceNumber ?? "",
            DocumentNumber = InvoiceReportText.DocumentNumber(i.PurchaseInvoice.TaxDocumentNumber, i.PurchaseInvoice.TaxDocumentSeries),
            Origin = DescribeOrigin(i.SalesInvoiceItem?.SalesInvoice),
            Product = InvoiceReportText.Product(i.ItemCode, i.ItemName),
            Quantity = i.Quantity,
            UnitOfMeasure = i.UnitOfMeasureCode ?? "",
            Value = i.GrandTotal,
            Status = InvoiceReportText.Status(i.PurchaseInvoice.InvoiceStatus),
        }).ToList();
    }

    /// <summary>
    /// "NF/Série de data" da nota original; sem NF, o número interno; sem vínculo, "—".
    /// </summary>
    private static string DescribeOrigin(SalesInvoice? original)
    {
        if (original is null)
            return InvoiceReportText.NoOrigin;

        var number = InvoiceReportText.DocumentNumber(original.TaxDocumentNumber, original.TaxDocumentSeries);
        if (number.Length == 0)
            number = original.InvoiceNumber ?? InvoiceReportText.NoOrigin;

        return original.InvoiceDate is null
            ? number
            : $"{number} de {InvoiceReportText.Date(original.InvoiceDate)}";
    }
}
