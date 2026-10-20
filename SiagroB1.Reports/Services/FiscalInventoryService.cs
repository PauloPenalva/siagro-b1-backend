using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Inventário em Data a custo PEPS: posição do estoque fiscal de uma filial ao fim de um dia, a partir
/// das camadas em aberto do <see cref="FiscalKardexService"/>. Nada é gravado.
/// </summary>
public class FiscalInventoryService(IUnitOfWork db, FiscalKardexService kardex)
{
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
}
