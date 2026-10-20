using System.Globalization;
using FastReport;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Inventário em Data a custo PEPS: posição do estoque fiscal de uma filial ao fim de um dia, a partir
/// das camadas em aberto do <see cref="FiscalKardexService"/>. Nada é gravado.
/// </summary>
public class FiscalInventoryService(IUnitOfWork db, FiscalKardexService kardex, FastReportService fastReport)
{
    public const string BookTemplate = "FiscalInventoryBook.frx";
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<FiscalInventoryData> BuildAsync(FiscalInventoryRequest request)
    {
        var date = request.Date.Date;
        var itemCode = string.IsNullOrWhiteSpace(request.ItemCode) ? null : request.ItemCode;
        var items = await kardex.OpenLayersAsync(request.BranchCode, itemCode, date);

        var codes = items.Select(x => x.ItemCode).ToList();
        var ncms = await db.Context.Items
            .AsNoTracking()
            .Where(i => codes.Contains(i.ItemCode))
            .ToDictionaryAsync(i => i.ItemCode, i => i.Ncm);

        var rows = new List<FiscalInventoryRowDto>();
        var pending = new List<string>();
        foreach (var item in items)
        {
            if (item.PendingValue > 0m)
                pending.Add($"{item.ItemCode} - {item.ItemName}");

            var quantity = item.Layers.Sum(l => l.Quantity) - item.Deficit;
            var value = item.Layers.Sum(l => l.Value);
            // só sai quem tem quantidade E valor zerados; saldo negativo (valor 0) entra
            if (quantity == 0m && value == 0m)
                continue;

            rows.Add(new FiscalInventoryRowDto
            {
                Ncm = ncms.GetValueOrDefault(item.ItemCode)?.Trim() ?? "",
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                UnitOfMeasureCode = item.UnitOfMeasureCode,
                Quantity = quantity,
                UnitCost = quantity > 0m ? Math.Round(value / quantity, 6) : null,
                Value = value,
            });
        }

        return new FiscalInventoryData
        {
            Date = date,
            BranchCode = request.BranchCode,
            // NCM vazio por último; depois código do produto
            Rows = rows.OrderBy(r => r.Ncm == "" ? 1 : 0).ThenBy(r => r.Ncm, StringComparer.Ordinal)
                .ThenBy(r => r.ItemCode, StringComparer.Ordinal).ToList(),
            TotalValue = rows.Sum(r => r.Value),
            PendingComplementItems = pending,
        };
    }

    /// <summary>PDF do Livro de Registro de Inventário (modelo 7): termo de abertura, inventário e termo de encerramento.</summary>
    public async Task<byte[]> BookAsync(FiscalInventoryBookRequest request)
    {
        using var report = await PrepareBookAsync(request);
        return FastReportService.ExportPdf(report, "Registro de Inventário");
    }

    /// <summary>Valida e prepara o livro sem exportar (as páginas preparadas são conferidas nos testes).</summary>
    public async Task<Report> PrepareBookAsync(FiscalInventoryBookRequest request)
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

        var rows = await BookRowsAsync(request.BranchCode, request.Date);
        if (rows.Count == 0)
            throw new BusinessException("Não há saldo a inventariar nesta data.");

        var date = request.Date.Date;
        var city = branch!.Municipality is { } m ? $"{m.Name}/{m.StateAbbreviation}" : "";
        var parameters = new Dictionary<string, object>
        {
            ["pLegalName"] = branch.LegalName!.Trim(),
            ["pTaxId"] = FormatTaxId(branch.TaxId!),
            ["pStateRegistration"] = branch.StateRegistration!.Trim(),
            ["pAddress"] = Address(branch, city),
            ["pCityDate"] = $"{(city == "" ? "" : city + ", ")}{DateTime.Today.ToString("dd/MM/yyyy", Culture)}",
            ["pInventoryDate"] = date.ToString("dd/MM/yyyy", Culture),
            ["pBookNumber"] = request.BookNumber,
            ["pFirstSheet"] = request.FirstSheet,
            ["pGrandTotal"] = Money(rows.Sum(r => r.Value)),
        };

        return await fastReport.PrepareAsync(BookTemplate, rows, "Rows", "Rows", parameters)
            ?? throw new InvalidOperationException("O FastReport não preparou o livro de inventário.");
    }

    /// <summary>
    /// Linhas do livro na ordem da grade, com o subtotal do NCM na última linha de cada NCM.
    /// Complemento de valor pendente fica de fora (já não entra no valor da linha).
    /// </summary>
    public async Task<List<FiscalInventoryBookRowDto>> BookRowsAsync(string branchCode, DateTime date)
    {
        var data = await BuildAsync(new FiscalInventoryRequest { BranchCode = branchCode, Date = date });
        var rows = data.Rows.Select(r => new FiscalInventoryBookRowDto
        {
            Ncm = r.Ncm,
            ItemCode = r.ItemCode,
            Description = $"{r.ItemCode} - {r.ItemName}",
            UnitOfMeasureCode = r.UnitOfMeasureCode ?? "",
            QuantityText = r.Quantity.ToString("N3", Culture),
            UnitCostText = r.UnitCost?.ToString("N6", Culture) ?? "",
            Value = r.Value,
            ValueText = Money(r.Value),
        }).ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            if (i + 1 < rows.Count && rows[i + 1].Ncm == rows[i].Ncm)
                continue;
            var ncm = rows[i].Ncm;
            var subtotal = rows.Where(r => r.Ncm == ncm).Sum(r => r.Value);
            rows[i].NcmGroupTotal = subtotal;
            rows[i].NcmGroupTotalText = Money(subtotal);
        }

        return rows;
    }

    private static string Money(decimal value) => value.ToString("N2", Culture);

    private static string FormatTaxId(string taxId)
    {
        var digits = new string(taxId.Where(char.IsDigit).ToArray());
        return digits.Length == 14
            ? Convert.ToUInt64(digits).ToString(@"00\.000\.000\/0000\-00", CultureInfo.InvariantCulture)
            : taxId.Trim();
    }

    private static string Address(Branch branch, string city)
    {
        var street = string.Join(", ", new[] { branch.Street, branch.StreetNumber, branch.Complement }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        var parts = new[] { street, branch.District?.Trim(), city,
                string.IsNullOrWhiteSpace(branch.ZipCode) ? null : "CEP " + branch.ZipCode.Trim() }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" - ", parts);
    }
}
