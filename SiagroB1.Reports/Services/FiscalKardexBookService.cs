using System.Globalization;
using FastReport;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Livro Registro Kardex — modelo 3 (Registro de Controle da Produção e do Estoque), com base no
/// MATR470 do Protheus: termo de abertura, uma ficha por produto × mês e termo de encerramento.
/// Os valores são os do <see cref="FiscalKardexService"/> (custo PEPS). Spec 2026-10-22.
/// </summary>
public class FiscalKardexBookService(IUnitOfWork db, FiscalKardexService kardex, FastReportService fastReport)
{
    public const string BookTemplate = "FiscalKardexBook.frx";
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<byte[]> BookAsync(FiscalKardexBookRequest request)
    {
        using var report = await PrepareBookAsync(request);
        return FastReportService.ExportPdf(report, "Registro Kardex");
    }

    /// <summary>Valida e prepara o livro sem exportar (as páginas preparadas são conferidas nos testes).</summary>
    public async Task<Report> PrepareBookAsync(FiscalKardexBookRequest request)
    {
        if (request.BookNumber < 1 || request.FirstSheet < 1)
            throw new BusinessException("Informe o número do livro e a folha inicial.");

        var branch = await db.Context.Branchs.AsNoTracking()
            .Include(b => b.Municipality)
            .FirstOrDefaultAsync(b => b.Code == request.BranchCode);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(branch?.LegalName)) missing.Add("Razão social");
        if (string.IsNullOrWhiteSpace(branch?.TaxId)) missing.Add("CNPJ");
        if (string.IsNullOrWhiteSpace(branch?.StateRegistration)) missing.Add("Inscrição Estadual");
        if (missing.Count > 0)
            throw new BusinessException($"Complete o cadastro da filial antes de imprimir: {string.Join(", ", missing)}.");

        var rows = await BookRowsAsync(request.BranchCode, request.ItemCode, request.FromDate, request.ToDate);
        if (rows.Count == 0)
            throw new BusinessException("Não há movimento de estoque no período.");

        var city = branch!.Municipality is { } m ? $"{m.Name}/{m.StateAbbreviation}" : "";
        var parameters = new Dictionary<string, object>
        {
            ["pLegalName"] = branch.LegalName!.Trim(),
            ["pBranchName"] = branch.BranchName?.Trim() ?? "",
            ["pTaxId"] = FiscalInventoryService.FormatTaxId(branch.TaxId!),
            ["pStateRegistration"] = branch.StateRegistration!.Trim(),
            ["pAddress"] = FiscalInventoryService.Address(branch, city),
            ["pCityDate"] = $"{(city == "" ? "" : city + ", ")}{DateTime.Today.ToString("dd/MM/yyyy", Culture)}",
            ["pPeriod"] = $"{request.FromDate.ToString("dd/MM/yyyy", Culture)} a {request.ToDate.ToString("dd/MM/yyyy", Culture)}",
            ["pBookNumber"] = request.BookNumber,
            ["pFirstSheet"] = request.FirstSheet,
        };

        return await fastReport.PrepareAsync(BookTemplate, rows, "Rows", "Rows", parameters)
            ?? throw new InvalidOperationException("O FastReport não preparou o livro Registro Kardex.");
    }

    /// <summary>
    /// Linhas do livro: para cada mês do período (cortado nas datas pedidas) e cada produto com saldo
    /// ou movimento, a linha de saldo inicial seguida dos movimentos do Kardex.
    /// </summary>
    public async Task<List<FiscalKardexBookRowDto>> BookRowsAsync(string branchCode, string? itemCode, DateTime fromDate, DateTime toDate)
    {
        var from = fromDate.Date;
        var to = toDate.Date;
        var rows = new List<FiscalKardexBookRowDto>();
        Dictionary<string, (string? Ncm, string? Account)>? items = null;

        for (var month = new DateTime(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var start = month < from ? from : month;
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var end = monthEnd > to ? to : monthEnd;

            var data = await kardex.BuildAsync(new FiscalKardexRequest
            {
                BranchCode = branchCode,
                ItemCode = string.IsNullOrWhiteSpace(itemCode) ? null : itemCode,
                FromDate = start,
                ToDate = end,
            });
            if (data.Items.Count == 0)
                continue;

            if (items is null || data.Items.Any(i => !items.ContainsKey(i.ItemCode)))
            {
                var codes = data.Items.Select(i => i.ItemCode).ToList();
                var found = await db.Context.Items.AsNoTracking()
                    .Where(i => codes.Contains(i.ItemCode))
                    .Select(i => new { i.ItemCode, i.Ncm, i.LedgerAccountCode })
                    .ToListAsync();
                items ??= [];
                foreach (var f in found)
                    items[f.ItemCode] = (f.Ncm, f.LedgerAccountCode);
            }

            foreach (var item in data.Items.OrderBy(i => i.ItemCode, StringComparer.Ordinal))
                rows.AddRange(Sheet(month, item, items.GetValueOrDefault(item.ItemCode)));
        }

        return rows;
    }

    private static List<FiscalKardexBookRowDto> Sheet(DateTime month, FiscalKardexItemDto item, (string? Ncm, string? Account) master)
    {
        var sheetKey = $"{month:yyyy-MM}|{item.ItemCode}";
        var period = month.ToString("MM/yyyy", Culture);
        var product = $"{item.ItemCode} - {item.ItemName}";
        var unit = item.UnitOfMeasureCode?.Trim() ?? "";
        var ncm = master.Ncm?.Trim() ?? "";
        var account = master.Account?.Trim() ?? "";

        FiscalKardexBookRowDto New() => new()
        {
            SheetKey = sheetKey, Period = period, Product = product, UnitOfMeasureCode = unit, Ncm = ncm,
        };

        var opening = New();
        opening.IsOpening = true;
        opening.LedgerAccount = "SALDO INICIAL:";
        opening.BalanceText = Quantity(item.OpeningQuantity);
        var sheet = new List<FiscalKardexBookRowDto> { opening };

        foreach (var line in item.Lines)
        {
            var isOut = line.Direction == "S";
            var isDocument = IsDocument(line.Kind);
            var row = New();
            row.Species = isDocument ? "NFE" : "";
            row.Series = isDocument ? line.Series : "";
            row.Number = isDocument ? line.Number : line.InternalNumber;
            row.DateText = line.Date.ToString("dd/MM/yyyy", Culture);
            row.Day = line.Date.ToString("dd", Culture);
            row.LedgerAccount = !isOut && line.Kind != "Saldo inicial" ? account : "";
            row.Cfop = line.Cfop;
            row.Direction = isOut ? "S" : "E";
            row.Code = isDocument && line.Kind != "Complemento de valor" ? "1" : "3";
            row.QuantityText = Quantity(isOut ? line.QuantityOut : line.QuantityIn);
            row.ValueText = Money(isOut ? line.ValueOut : line.ValueIn);
            row.IpiText = Money(0m);
            row.BalanceText = Quantity(line.BalanceQuantity);
            row.Notes = Notes(line);
            sheet.Add(row);
        }

        var totalInQuantity = item.Lines.Sum(l => l.QuantityIn);
        var totalInValue = item.Lines.Sum(l => l.ValueIn);
        var totalOutQuantity = item.Lines.Sum(l => l.QuantityOut);
        var totalOutValue = item.Lines.Sum(l => l.ValueOut);
        foreach (var row in sheet)
        {
            row.TotalInQuantityText = Quantity(totalInQuantity);
            row.TotalInValueText = Money(totalInValue);
            row.TotalOutQuantityText = Quantity(totalOutQuantity);
            row.TotalOutValueText = Money(totalOutValue);
            row.ClosingBalanceText = Quantity(item.ClosingQuantity);
        }

        return sheet;
    }

    /// <summary>Linhas que vêm de nota fiscal (o resto é ajuste manual ou saldo inicial).</summary>
    private static bool IsDocument(string kind) => kind is "Entrada" or "Saída" or "Devolução de compra"
        or "Devolução de venda" or "Complemento de valor";

    private static string Notes(FiscalKardexLineDto line) => line.Kind switch
    {
        "Saldo inicial" => "Saldo inicial",
        "Complemento de valor" => "Complemento de valor",
        "Devolução de compra" => "Devolução de compra",
        "Devolução de venda" => "Devolução de venda",
        // no ajuste o motivo vem no lugar da natureza
        "Ajuste manual — entrada" or "Ajuste manual — saída" => string.IsNullOrWhiteSpace(line.UsageName)
            ? "Ajuste manual"
            : $"Ajuste manual: {line.UsageName.Trim()}",
        _ => "",
    };

    private static string Quantity(decimal value) => value.ToString("N3", Culture);
    private static string Money(decimal value) => value.ToString("N2", Culture);
}
