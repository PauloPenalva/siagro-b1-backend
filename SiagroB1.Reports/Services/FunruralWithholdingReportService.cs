using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Apuração e conferência do Funrural (INSS, GILRAT e SENAR) retido por item de Documento de
/// Entrada. Lê a fotografia gravada na linha por <c>PurchaseInvoiceFunrural</c> — nunca recalcula —
/// e abate na linha de origem as devoluções de compra (<c>IsNfeReturn</c>) que apontam para ela,
/// qualquer que seja a data da devolução.
/// </summary>
public class FunruralWithholdingReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>NF-e que não vale como devolução: o estorno do Funrural também não vale.</summary>
    private static readonly NfeStatus[] VoidNfeStatuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<FunruralWithholdingData> BuildAsync(FunruralWithholdingRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        // Devolução de compra e devolução do cliente são InvoiceType.Return: nunca são linha de origem.
        // O complemento de valor (D11) entra.
        var query = db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Include(i => i.PurchaseContract)
            .Where(i => i.PurchaseInvoice != null)
            .Where(i => i.PurchaseInvoice!.InvoiceType != PurchaseInvoiceType.Return)
            .Where(i => i.FunruralInssValue + i.FunruralRatValue + i.FunruralSenarValue > 0)
            .Where(i => i.PurchaseInvoice!.IssueDate >= from && i.PurchaseInvoice.IssueDate < toExclusive)
            .Where(i => statuses.Contains(i.PurchaseInvoice!.InvoiceStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.PurchaseInvoice!.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.PurchaseInvoice!.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(i => i.PurchaseContract != null && i.PurchaseContract.Code == request.ContractCode);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(i => nfe.Contains(i.PurchaseInvoice!.NfeStatus));
        }

        var items = await query.ToListAsync();
        var returnsByOrigin = await LoadReturnsAsync(items.Select(i => i.Key).ToArray(), statuses);

        var groupOf = InvoiceItemGrouping.BuildGroupResolver(items, i => i.ItemCode, i => i.ItemName, i => i.UnitOfMeasureCode);

        var rows = new List<FunruralWithholdingRowDto>();
        var gross = FunruralAmounts.Zero;
        var returned = FunruralAmounts.Zero;

        var ordered = items
            .OrderBy(i => i.PurchaseInvoice!.IssueDate)
            .ThenBy(i => i.PurchaseInvoice!.InvoiceNumber ?? "", StringComparer.Ordinal)
            .ThenBy(i => groupOf(i), StringComparer.CurrentCultureIgnoreCase);

        foreach (var item in ordered)
        {
            var itemAmounts = Amounts(item);
            gross += itemAmounts;
            rows.Add(ItemRow(item, groupOf(item)));

            if (!returnsByOrigin.TryGetValue(item.Key, out var returns))
                continue;

            var net = itemAmounts;
            foreach (var ret in returns)
            {
                var deducted = -Amounts(ret);
                net += deducted;
                returned += deducted;
                rows.Add(ReturnRow(ret, deducted, item));
            }

            var netRow = AmountsRow(FunruralRowKind.Net, "  (=) Líquido do item", net);
            netRow.CardCode = item.PurchaseInvoice!.CardCode;
            rows.Add(netRow);
        }

        return new FunruralWithholdingData(rows, items.Count, gross, returned);
    }

    public async Task<byte[]> ExecuteAsync(FunruralWithholdingRequest request)
    {
        var data = await BuildAsync(request);
        var standalone = Standalone;
        var first = data.Rows.FirstOrDefault(r => r.Kind == FunruralRowKind.Item);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Emitente", first?.Partner, ProductName(first), null, extra),
            [FastReportService.StandaloneParameter] = standalone,
            ["pItemCount"] = data.ItemCount,
        };

        AddTotals(parameters, "Gross", data.Gross);
        AddTotals(parameters, "Returns", data.Returns);
        AddTotals(parameters, "Net", data.Net);

        return await reportService.GeneratePdfAsync(
            "FunruralWithholding.frx", data.Rows, "FunruralRows", "FunruralRows", parameters);
    }

    /// <summary>
    /// Linhas de devolução de compra por linha de origem, em ordem de emissão. Usa o mesmo filtro de
    /// situação das entradas; NF-e cancelada, denegada ou inutilizada não abate.
    /// </summary>
    private async Task<Dictionary<Guid?, List<PurchaseInvoiceItem>>> LoadReturnsAsync(Guid?[] originKeys, InvoiceStatus[] statuses)
    {
        if (originKeys.Length == 0)
            return [];

        var returns = await db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Where(i => i.PurchaseInvoiceItemOriginKey != null && originKeys.Contains(i.PurchaseInvoiceItemOriginKey))
            .Where(i => i.PurchaseInvoice != null && i.PurchaseInvoice.IsNfeReturn)
            .Where(i => statuses.Contains(i.PurchaseInvoice!.InvoiceStatus))
            .Where(i => !VoidNfeStatuses.Contains(i.PurchaseInvoice!.NfeStatus))
            .ToListAsync();

        return returns
            .OrderBy(i => i.PurchaseInvoice!.IssueDate)
            .ThenBy(i => i.PurchaseInvoice!.InvoiceNumber ?? "", StringComparer.Ordinal)
            .GroupBy(i => i.PurchaseInvoiceItemOriginKey)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static void AddTotals(Dictionary<string, object> parameters, string name, FunruralAmounts amounts)
    {
        parameters[$"p{name}Base"] = amounts.Base;
        parameters[$"p{name}Inss"] = amounts.InssValue;
        parameters[$"p{name}Rat"] = amounts.RatValue;
        parameters[$"p{name}Senar"] = amounts.SenarValue;
        parameters[$"p{name}Total"] = amounts.Total;
    }

    /// <summary>Nome do produto filtrado, sem o sufixo " - UM" do grupo.</summary>
    private static string? ProductName(FunruralWithholdingRowDto? row)
    {
        if (row is null)
            return null;

        var cut = row.Description.LastIndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? row.Description[..cut] : row.Description;
    }

    private static FunruralAmounts Amounts(PurchaseInvoiceItem i) =>
        new(i.FunruralBase, i.FunruralInssValue, i.FunruralRatValue, i.FunruralSenarValue);

    private static FunruralWithholdingRowDto ItemRow(PurchaseInvoiceItem i, string product)
    {
        var row = AmountsRow(FunruralRowKind.Item, product, Amounts(i));
        row.IssueDate = InvoiceReportText.Date(i.PurchaseInvoice!.IssueDate);
        row.InternalNumber = i.PurchaseInvoice.InvoiceNumber ?? "";
        row.DocumentNumber = InvoiceReportText.DocumentNumber(i.PurchaseInvoice.TaxDocumentNumber, i.PurchaseInvoice.TaxDocumentSeries);
        row.Partner = InvoiceReportText.Partner(i.PurchaseInvoice.CardCode, i.PurchaseInvoice.CardName);
        row.CardCode = i.PurchaseInvoice.CardCode;
        row.InssRate = Rate(i.FunruralInssRate);
        row.RatRate = Rate(i.FunruralRatRate);
        row.SenarRate = Rate(i.FunruralSenarRate);
        return row;
    }

    private static FunruralWithholdingRowDto ReturnRow(PurchaseInvoiceItem ret, FunruralAmounts deducted, PurchaseInvoiceItem origin)
    {
        var row = AmountsRow(FunruralRowKind.Return, "  (-) Devolução", deducted);
        row.IssueDate = InvoiceReportText.Date(ret.PurchaseInvoice!.IssueDate);
        row.CardCode = origin.PurchaseInvoice!.CardCode;
        row.InternalNumber = ret.PurchaseInvoice.InvoiceNumber ?? "";
        row.DocumentNumber = InvoiceReportText.DocumentNumber(ret.PurchaseInvoice.TaxDocumentNumber, ret.PurchaseInvoice.TaxDocumentSeries);
        return row;
    }

    private static FunruralWithholdingRowDto AmountsRow(FunruralRowKind kind, string description, FunruralAmounts a) => new()
    {
        Kind = kind,
        Description = description,
        Base = a.Base,
        InssValue = a.InssValue,
        RatValue = a.RatValue,
        SenarValue = a.SenarValue,
        Total = a.Total,
    };

    private static string Rate(decimal rate) => rate.ToString("0.00##", Culture);
}
