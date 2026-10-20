using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Fiscal;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Kardex Fiscal valorizado a PEPS por filial + produto (spec 2026-10-20). Lê as linhas
/// escrituráveis que movimentam estoque fiscal e o saldo inicial lançado, e entrega ao
/// <see cref="FifoLedger"/>. Tudo é recalculado do saldo inicial (ou do primeiro movimento) a cada
/// consulta; nada é gravado.
/// </summary>
public class FiscalKardexService(IUnitOfWork db)
{
    private const string WarningInsufficient = "Saldo insuficiente";
    private const string WarningDifferentUnit = "Unidade diferente";
    private const string WarningUnlinkedEntry = "Entrada sem vínculo valorizada pelo preço da nota";
    private const string WarningCostNotFound = "Custo da venda de origem não encontrado";
    private const string WarningPendingComplement = "Complemento sem saldo: somado à próxima entrada";

    /// <summary>Metadados de exibição levados pelo motor em <see cref="InventoryMovement.Source"/>.</summary>
    private sealed record LineSource(
        string InternalNumber,
        string DocumentNumber,
        string Partner,
        string Cfop,
        string UsageName,
        decimal InvoiceValue,
        string? ItemName,
        bool UnlinkedEntry = false);

    /// <summary>Movimentos de um produto, já sem o que está até a data do saldo inicial.</summary>
    private sealed record ItemLedger(
        string ItemCode,
        string ItemName,
        string? UnitOfMeasureCode,
        FiscalInventoryOpeningBalance? Opening,
        FifoResult Result);

    public async Task<FiscalKardexData> BuildAsync(FiscalKardexRequest request)
    {
        var from = request.FromDate.Date;
        var ledgers = await LoadLedgersAsync(request.BranchCode, request.ItemCode, request.ToDate.Date);

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
        (await LoadLedgersAsync(branchCode, itemCode, atDate.Date))
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

    private async Task<List<ItemLedger>> LoadLedgersAsync(string branchCode, string? itemCode, DateTime toDate)
    {
        var toExclusive = toDate.AddDays(1);
        var item = string.IsNullOrWhiteSpace(itemCode) ? null : itemCode;

        var openings = await db.Context.FiscalInventoryOpeningBalances
            .AsNoTracking()
            .Where(o => o.BranchCode == branchCode && o.BalanceDate < toExclusive)
            .Where(o => item == null || o.ItemCode == item)
            .ToDictionaryAsync(o => o.ItemCode);

        var sales = await BookableInvoiceLines.Sales(db.Context.SalesInvoicesItems
                .AsNoTracking()
                .Include(i => i.SalesInvoice))
            .Where(i => i.SalesInvoice!.BranchCode == branchCode)
            .Where(i => i.SalesInvoice!.InvoiceDate < toExclusive)
            .Where(i => i.SalesInvoice!.InvoiceType != SalesInvoiceType.IcmsComplement)
            .Where(i => i.MovesFiscalInventory && i.ItemCode != null && i.ItemCode != "")
            .Where(i => item == null || i.ItemCode == item)
            .ToListAsync();

        var purchases = await BookableInvoiceLines.Purchases(db.Context.PurchaseInvoicesItems
                .AsNoTracking()
                .Include(i => i.PurchaseInvoice))
            .Where(i => i.PurchaseInvoice!.BranchCode == branchCode)
            .Where(i => (i.PurchaseInvoice!.PostingDate ?? i.PurchaseInvoice.IssueDate) < toExclusive)
            .Where(i => i.PurchaseInvoice!.InvoiceType != PurchaseInvoiceType.IcmsComplement)
            .Where(i => i.MovesFiscalInventory && i.ItemCode != null && i.ItemCode != "")
            .Where(i => item == null || i.ItemCode == item)
            .ToListAsync();

        var movements = sales.Select(s => (Code: s.ItemCode, Movement: SaleMovement(s)))
            .Concat(purchases.Select(p => (Code: p.ItemCode!, Movement: PurchaseMovement(p))))
            // nº interno em ordem numérica: "9" antes de "10" (comprimento, depois ordinal)
            .OrderBy(m => ((LineSource)m.Movement.Source!).InternalNumber.Length)
            .ThenBy(m => ((LineSource)m.Movement.Source!).InternalNumber, StringComparer.Ordinal)
            .ThenBy(m => m.Movement.LineKey)
            .Select((m, index) => (m.Code, Movement: m.Movement with { DaySequence = index }))
            .ToList();

        var codes = movements.Select(m => m.Code).Concat(openings.Keys).Distinct().Order(StringComparer.Ordinal);

        var ledgers = new List<ItemLedger>();
        foreach (var code in codes)
        {
            openings.TryGetValue(code, out var opening);
            var own = movements
                .Where(m => m.Code == code)
                .Select(m => m.Movement)
                .Where(m => opening == null || m.Date > opening.BalanceDate.Date)
                .ToList();
            if (opening != null)
                own.Add(OpeningMovement(opening));

            var chronological = own.OrderBy(m => m.Date).ThenBy(m => m.Kind == InventoryMovementKind.Opening ? 0 : 1)
                .ThenBy(m => m.DaySequence).ToList();
            var unit = opening?.UnitOfMeasureCode ?? chronological.Select(m => m.UnitOfMeasure).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            var name = chronological.Select(m => ((LineSource)m.Source!).ItemName).LastOrDefault(n => !string.IsNullOrWhiteSpace(n))
                       ?? opening?.ItemName ?? "";

            ledgers.Add(new ItemLedger(code, name, unit, opening, FifoLedger.Run(own)));
        }

        return ledgers;
    }

    private static InventoryMovement OpeningMovement(FiscalInventoryOpeningBalance o) => new(
        o.Key, o.BalanceDate.Date, -1, InventoryMovementKind.Opening, o.Quantity, o.TotalValue, null,
        o.UnitOfMeasureCode, new LineSource("", "", "", "", "", 0m, o.ItemName));

    /// <summary>Linha de saída: devolução de venda (CFOP de entrada + origem) entra pelo custo da saída de origem.</summary>
    private static InventoryMovement SaleMovement(SalesInvoiceItem i)
    {
        var invoice = i.SalesInvoice!;
        var direction = DirectionOf(i.Cfop, "S");
        var kind = direction == "E"
            ? invoice.InvoiceType == SalesInvoiceType.Return && i.SalesInvoiceItemOriginKey != null
                ? InventoryMovementKind.SalesReturn
                : InventoryMovementKind.Entry
            : InventoryMovementKind.Exit;
        var origin = kind == InventoryMovementKind.SalesReturn ? i.SalesInvoiceItemOriginKey : null;
        var value = direction == "E" ? i.GrandTotal - i.IcmsValue : 0m;

        return new InventoryMovement(i.Key ?? Guid.Empty, (invoice.InvoiceDate ?? default).Date, 0, kind, i.Quantity, value, origin,
            i.UnitOfMeasureCode,
            new LineSource(invoice.InvoiceNumber ?? "", DocumentNumber(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber),
                Partner(invoice.CardCode, invoice.CardName), i.Cfop ?? "", i.UsageName ?? "", i.GrandTotal, i.ItemName,
                UnlinkedEntry: kind == InventoryMovementKind.Entry));
    }

    /// <summary>
    /// Linha de entrada: complemento de valor soma na camada de origem; devolução de compra (CFOP de
    /// saída + origem) baixa da camada de origem; entrada com vínculo a linha de saída
    /// (<c>SalesInvoiceItemKey</c>, devolução de venda emitida pelo cliente) entra pelo custo da saída.
    /// </summary>
    private static InventoryMovement PurchaseMovement(PurchaseInvoiceItem i)
    {
        var invoice = i.PurchaseInvoice!;
        var direction = DirectionOf(i.Cfop, "E");
        InventoryMovementKind kind;
        Guid? origin = null;
        if (invoice.InvoiceType == PurchaseInvoiceType.ValueComplement)
        {
            kind = InventoryMovementKind.ValueComplement;
            origin = i.ComplementedItemKey;
        }
        else if (direction == "S")
        {
            var isReturn = invoice.InvoiceType == PurchaseInvoiceType.Return && i.PurchaseInvoiceItemOriginKey != null;
            kind = isReturn ? InventoryMovementKind.PurchaseReturn : InventoryMovementKind.Exit;
            origin = isReturn ? i.PurchaseInvoiceItemOriginKey : null;
        }
        else if (i.SalesInvoiceItemKey != null)
        {
            kind = InventoryMovementKind.SalesReturn;
            origin = i.SalesInvoiceItemKey;
        }
        else
            kind = InventoryMovementKind.Entry;

        var quantity = kind == InventoryMovementKind.ValueComplement ? 0m : i.Quantity;
        var value = direction == "E" || kind == InventoryMovementKind.ValueComplement ? i.GrandTotal - i.IcmsValue : 0m;

        return new InventoryMovement(i.Key ?? Guid.Empty, (invoice.PostingDate ?? invoice.IssueDate ?? default).Date, 0, kind, quantity, value,
            origin, i.UnitOfMeasureCode,
            new LineSource(invoice.InvoiceNumber ?? "", DocumentNumber(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber),
                Partner(invoice.CardCode, invoice.CardName), i.Cfop ?? "", i.UsageName ?? "", i.GrandTotal, i.ItemName,
                UnlinkedEntry: kind == InventoryMovementKind.Entry && i.PurchaseInvoiceItemOriginKey == null && IsRemittanceCfop(i.Cfop)));
    }

    /// <summary>CFOP 1.9xx/2.9xx (remessas e retornos): sem origem, o preço da nota não é custo confiável.</summary>
    private static bool IsRemittanceCfop(string? cfop) =>
        cfop?.Trim() is { } c && (c.StartsWith("19") || c.StartsWith("29"));

    private static FiscalKardexLineDto Line(ValuedMovement v, string? itemUnit)
    {
        var m = v.Movement;
        var source = (LineSource)m.Source!;
        var warnings = new List<string>();
        if (v.InsufficientBalance) warnings.Add(WarningInsufficient);
        if (v.PendingValue) warnings.Add(WarningPendingComplement);
        if (v.CostNotFound) warnings.Add(WarningCostNotFound);
        if (source.UnlinkedEntry && !v.IsNegativeBalanceAdjustment) warnings.Add(WarningUnlinkedEntry);
        if (!v.IsNegativeBalanceAdjustment && !string.IsNullOrWhiteSpace(m.UnitOfMeasure) && !string.IsNullOrWhiteSpace(itemUnit)
            && !string.Equals(m.UnitOfMeasure.Trim(), itemUnit.Trim(), StringComparison.OrdinalIgnoreCase))
            warnings.Add(WarningDifferentUnit);

        var isExit = v.IsNegativeBalanceAdjustment
                     || m.Kind is InventoryMovementKind.Exit or InventoryMovementKind.PurchaseReturn;

        return new FiscalKardexLineDto
        {
            Date = m.Date,
            Direction = isExit ? "S" : "E",
            Kind = v.IsNegativeBalanceAdjustment ? "Acerto de saldo negativo" : KindText(m.Kind),
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
            InvoiceValue = v.IsNegativeBalanceAdjustment ? 0m : source.InvoiceValue,
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

    /// <summary>1/2/3 = entrada, 5/6/7 = saída; sem CFOP vale a direção do documento.</summary>
    private static string DirectionOf(string? cfop, string documentDirection) => cfop?.Trim() switch
    {
        [var first, ..] when first is '1' or '2' or '3' => "E",
        [var first, ..] when first is '5' or '6' or '7' => "S",
        _ => documentDirection,
    };

    private static string DocumentNumber(string? number, string? series, string? internalNumber)
    {
        var n = string.IsNullOrWhiteSpace(number) ? internalNumber ?? "" : number;
        return string.IsNullOrWhiteSpace(series) ? n : $"{n}/{series}";
    }

    private static string Partner(string code, string? name) => string.IsNullOrWhiteSpace(name) ? code : $"({code}) {name}";
}
