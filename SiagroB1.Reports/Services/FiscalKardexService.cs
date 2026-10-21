using SiagroB1.Infra.FiscalStock;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Kardex Fiscal valorizado a PEPS por filial + produto (spec 2026-10-20). Lê as linhas
/// escrituráveis que movimentam estoque fiscal e o saldo inicial lançado, e entrega ao
/// <see cref="FifoLedger"/>. Tudo é recalculado do saldo inicial (ou do primeiro movimento) a cada
/// consulta; nada é gravado.
/// </summary>
public class FiscalKardexService(FiscalStockLedgerLoader loader)
{
    private const string WarningInsufficient = "Saldo insuficiente";
    private const string WarningDifferentUnit = "Unidade diferente";
    private const string WarningUnlinkedEntry = "Entrada sem vínculo valorizada pelo preço da nota";
    private const string WarningCostNotFound = "Custo da venda de origem não encontrado";
    private const string WarningPendingComplement = "Complemento sem saldo: somado à próxima entrada";

    public async Task<FiscalKardexData> BuildAsync(FiscalKardexRequest request)
    {
        var from = request.FromDate.Date;
        var ledgers = await loader.LoadAsync(request.BranchCode, request.ItemCode, request.ToDate.Date);

        var items = new List<FiscalKardexItemDto>();
        foreach (var ledger in ledgers)
        {
            var lines = ledger.Result.Lines;
            var previous = lines.LastOrDefault(l => l.Movement.Date < from);
            var period = lines.Where(l => l.Movement.Date >= from).ToList();
            var last = lines.LastOrDefault();

            var openingQuantity = previous?.BalanceQuantity ?? 0m;
            var openingValue = previous?.BalanceValue ?? 0m;
            if (period.Count == 0 && openingQuantity == 0m && openingValue == 0m)
                continue;

            items.Add(new FiscalKardexItemDto
            {
                ItemCode = ledger.ItemCode,
                ItemName = ledger.ItemName,
                UnitOfMeasureCode = ledger.UnitOfMeasureCode,
                OpeningQuantity = openingQuantity,
                OpeningValue = openingValue,
                Lines = period.Select(l => Line(l, ledger.UnitOfMeasureCode)).ToList(),
                ClosingQuantity = last?.BalanceQuantity ?? 0m,
                ClosingValue = last?.BalanceValue ?? 0m,
            });
        }

        return new FiscalKardexData { Items = items };
    }

    /// <summary>
    /// Camadas PEPS em aberto ao fim do dia <paramref name="atDate"/> (base do Inventário em Data).
    /// A chave da camada é a linha de nota que a criou, ou a chave do saldo inicial.
    /// </summary>
    /// Um registro por produto com saldo inicial ou movimento até a data, ordenado por ItemCode.
    public async Task<List<FiscalInventoryItemLayers>> OpenLayersAsync(string branchCode, string? itemCode, DateTime atDate) =>
        (await loader.LoadAsync(branchCode, itemCode, atDate.Date))
            .Select(l => new FiscalInventoryItemLayers
            {
                ItemCode = l.ItemCode,
                ItemName = l.ItemName,
                UnitOfMeasureCode = l.UnitOfMeasureCode,
                Layers = l.Result.OpenLayers,
                Deficit = l.Result.Deficit,
                PendingValue = l.Result.PendingValue,
            })
            .ToList();

    private static FiscalKardexLineDto Line(ValuedMovement v, string? itemUnit)
    {
        var m = v.Movement;
        var source = (FiscalStockLineSource)m.Source!;
        var warnings = new List<string>();
        if (v.InsufficientBalance) warnings.Add(WarningInsufficient);
        if (v.PendingValue) warnings.Add(WarningPendingComplement);
        if (v.CostNotFound) warnings.Add(WarningCostNotFound);
        if (source.UnlinkedEntry) warnings.Add(WarningUnlinkedEntry);
        if (!string.IsNullOrWhiteSpace(m.UnitOfMeasure) && !string.IsNullOrWhiteSpace(itemUnit)
            && !string.Equals(m.UnitOfMeasure.Trim(), itemUnit.Trim(), StringComparison.OrdinalIgnoreCase))
            warnings.Add(WarningDifferentUnit);

        var isExit = m.Kind is InventoryMovementKind.Exit or InventoryMovementKind.PurchaseReturn
                     or InventoryMovementKind.AdjustmentOut;

        return new FiscalKardexLineDto
        {
            Date = m.Date,
            Direction = isExit ? "S" : "E",
            Kind = KindText(m.Kind),
            InternalNumber = source.InternalNumber,
            DocumentNumber = source.DocumentNumber,
            Partner = source.Partner,
            Cfop = source.Cfop,
            UsageName = source.UsageName,
            QuantityIn = v.QuantityIn,
            QuantityOut = v.QuantityOut,
            UnitCost = v.UnitCost,
            ValueIn = v.ValueIn,
            ValueOut = v.ValueOut,
            BalanceQuantity = v.BalanceQuantity,
            BalanceValue = v.BalanceValue,
            InvoiceValue = source.InvoiceValue,
            Warning = warnings.Count == 0 ? null : string.Join("; ", warnings),
        };
    }

    private static string KindText(InventoryMovementKind kind) => kind switch
    {
        InventoryMovementKind.Opening => "Saldo inicial",
        InventoryMovementKind.Entry => "Entrada",
        InventoryMovementKind.Exit => "Saída",
        InventoryMovementKind.PurchaseReturn => "Devolução de compra",
        InventoryMovementKind.SalesReturn => "Devolução de venda",
        InventoryMovementKind.ValueComplement => "Complemento de valor",
        _ => "",
    };
}
