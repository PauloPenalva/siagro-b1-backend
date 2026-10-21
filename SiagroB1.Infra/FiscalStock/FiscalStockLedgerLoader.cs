using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Infra.FiscalStock;

/// <summary>Metadados de exibição levados pelo motor em <see cref="InventoryMovement.Source"/>.</summary>
public sealed record FiscalStockLineSource(
    string InternalNumber,
    string DocumentNumber,
    string Partner,
    string Cfop,
    string UsageName,
    decimal InvoiceValue,
    string? ItemName,
    bool UnlinkedEntry = false);

/// <summary>Movimentos de um produto, já sem o que está até a data do saldo inicial.</summary>
public sealed record FiscalStockItemLedger(
    string ItemCode,
    string ItemName,
    string? UnitOfMeasureCode,
    FiscalInventoryOpeningBalance? Opening,
    FifoResult Result,
    decimal SeedQuantity = 0m,
    decimal SeedValue = 0m);

/// <summary>
/// Carrega as linhas escrituráveis que movimentam estoque fiscal e o saldo inicial lançado, e as
/// entrega ao <see cref="FifoLedger"/>. Tudo é recalculado a cada consulta; nada é gravado.
/// </summary>
public class FiscalStockLedgerLoader(IUnitOfWork db)
{
    /// <param name="fromDate">
    /// Início da consulta: o último mês FECHADO da filial que termina antes dele (ou de
    /// <paramref name="toDate"/>) vira a partida — as camadas do instantâneo semeiam o PEPS e só
    /// entram movimentos posteriores ao fim do mês; saldo inicial até essa data é ignorado.
    /// </param>
    public async Task<List<FiscalStockItemLedger>> LoadAsync(string branchCode, string? itemCode, DateTime toDate, DateTime? fromDate = null)
    {
        var toExclusive = toDate.AddDays(1);
        var item = string.IsNullOrWhiteSpace(itemCode) ? null : itemCode;

        var limit = (fromDate ?? toDate).Date;
        var basePeriod = (await db.Context.FiscalStockPeriods
                .AsNoTracking()
                .Where(p => p.BranchCode == branchCode && p.Status == FiscalStockPeriodStatus.Closed)
                .ToListAsync())
            .Select(p => (Period: p, End: new DateTime(p.Year, p.Month, 1).AddMonths(1).AddDays(-1)))
            .Where(p => p.End < limit)
            .OrderByDescending(p => p.End)
            .FirstOrDefault();
        DateTime? baseEnd = basePeriod.Period == null ? null : basePeriod.End;
        var baseExclusive = baseEnd?.AddDays(1) ?? DateTime.MinValue;

        var seeds = basePeriod.Period == null
            ? new Dictionary<string, List<FiscalStockPeriodLayer>>()
            : (await db.Context.FiscalStockPeriodLayers
                    .AsNoTracking()
                    .Where(l => l.PeriodKey == basePeriod.Period.Key)
                    .Where(l => item == null || l.ItemCode == item)
                    .ToListAsync())
                .GroupBy(l => l.ItemCode)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Sequence).ToList());

        var openings = await db.Context.FiscalInventoryOpeningBalances
            .AsNoTracking()
            .Where(o => o.BranchCode == branchCode && o.BalanceDate < toExclusive && o.BalanceDate >= baseExclusive)
            .Where(o => item == null || o.ItemCode == item)
            .ToDictionaryAsync(o => o.ItemCode);

        var adjustments = await db.Context.FiscalStockAdjustments
            .AsNoTracking()
            .Where(a => a.BranchCode == branchCode && a.AdjustmentDate < toExclusive && a.AdjustmentDate >= baseExclusive)
            .Where(a => item == null || a.ItemCode == item)
            .ToListAsync();

        var sales = await BookableInvoiceLines.Sales(db.Context.SalesInvoicesItems
                .AsNoTracking()
                .Include(i => i.SalesInvoice))
            .Where(i => i.SalesInvoice!.BranchCode == branchCode)
            .Where(i => i.SalesInvoice!.InvoiceDate < toExclusive && i.SalesInvoice.InvoiceDate >= baseExclusive)
            .Where(i => i.SalesInvoice!.InvoiceType != SalesInvoiceType.IcmsComplement)
            .Where(i => i.MovesFiscalInventory && i.ItemCode != null && i.ItemCode != "")
            .Where(i => item == null || i.ItemCode == item)
            .ToListAsync();

        var purchases = await BookableInvoiceLines.Purchases(db.Context.PurchaseInvoicesItems
                .AsNoTracking()
                .Include(i => i.PurchaseInvoice))
            .Where(i => i.PurchaseInvoice!.BranchCode == branchCode)
            .Where(i => (i.PurchaseInvoice!.PostingDate ?? i.PurchaseInvoice.IssueDate) < toExclusive
                        && (i.PurchaseInvoice.PostingDate ?? i.PurchaseInvoice.IssueDate) >= baseExclusive)
            .Where(i => i.PurchaseInvoice!.InvoiceType != PurchaseInvoiceType.IcmsComplement)
            .Where(i => i.MovesFiscalInventory && i.ItemCode != null && i.ItemCode != "")
            .Where(i => item == null || i.ItemCode == item)
            .ToListAsync();

        var movements = sales.Select(s => (Code: s.ItemCode, Movement: SaleMovement(s)))
            .Concat(purchases.Select(p => (Code: p.ItemCode!, Movement: PurchaseMovement(p))))
            .Concat(adjustments.Select(a => (Code: a.ItemCode, Movement: AdjustmentMovement(a))))
            // nº interno em ordem numérica: "9" antes de "10" (comprimento, depois ordinal)
            .OrderBy(m => ((FiscalStockLineSource)m.Movement.Source!).InternalNumber.Length)
            .ThenBy(m => ((FiscalStockLineSource)m.Movement.Source!).InternalNumber, StringComparer.Ordinal)
            .ThenBy(m => m.Movement.LineKey)
            .Select((m, index) => (m.Code, Movement: m.Movement with { DaySequence = index }))
            .ToList();

        var codes = movements.Select(m => m.Code).Concat(openings.Keys).Concat(seeds.Keys).Distinct().Order(StringComparer.Ordinal);

        var ledgers = new List<FiscalStockItemLedger>();
        foreach (var code in codes)
        {
            openings.TryGetValue(code, out var opening);
            seeds.TryGetValue(code, out var seedLayers);
            // saldo inicial depois do mês fechado substitui a história, inclusive o instantâneo
            if (seedLayers == null || opening != null) seedLayers = [];
            var own = movements
                .Where(m => m.Code == code)
                .Select(m => m.Movement)
                .Where(m => opening == null || m.Date > opening.BalanceDate.Date)
                .ToList();
            if (opening != null)
                own.Add(OpeningMovement(opening));

            var chronological = own.OrderBy(m => m.Date).ThenBy(m => m.Kind == InventoryMovementKind.Opening ? 0 : 1)
                .ThenBy(m => m.DaySequence).ToList();
            var unit = opening?.UnitOfMeasureCode
                       ?? seedLayers.Select(l => l.UnitOfMeasureCode).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                       ?? chronological.Select(m => m.UnitOfMeasure).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            var name = chronological.Select(m => ((FiscalStockLineSource)m.Source!).ItemName).LastOrDefault(n => !string.IsNullOrWhiteSpace(n))
                       ?? opening?.ItemName
                       ?? seedLayers.Select(l => l.ItemName).LastOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";

            var seed = seedLayers.Select(l => new FifoLayer(l.SourceLineKey, l.LayerDate.Date, l.Quantity, l.Value)).ToList();
            ledgers.Add(new FiscalStockItemLedger(code, name, unit, opening, FifoLedger.Run(chronological, seed),
                seed.Sum(l => l.Quantity), seed.Sum(l => l.Value)));
        }

        return ledgers;
    }

    private static InventoryMovement OpeningMovement(FiscalInventoryOpeningBalance o) => new(
        o.Key, o.BalanceDate.Date, -1, InventoryMovementKind.Opening, o.Quantity, o.TotalValue, null,
        o.UnitOfMeasureCode, new FiscalStockLineSource("", "", "", "", "", 0m, o.ItemName));

    /// <summary>Ajuste manual: nº do ajuste como nº interno e o motivo no lugar da natureza.</summary>
    private static InventoryMovement AdjustmentMovement(FiscalStockAdjustment a) => new(
        a.Key, a.AdjustmentDate.Date, 0,
        a.Direction == FiscalStockAdjustmentDirection.Out ? InventoryMovementKind.AdjustmentOut : InventoryMovementKind.AdjustmentIn,
        a.Quantity, a.TotalValue, null, a.UnitOfMeasureCode,
        new FiscalStockLineSource(a.DocNumber, a.DocNumber, "", "", a.Reason, a.TotalValue, a.ItemName));

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
            new FiscalStockLineSource(invoice.InvoiceNumber ?? "", DocumentNumber(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber),
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
            new FiscalStockLineSource(invoice.InvoiceNumber ?? "", DocumentNumber(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber),
                Partner(invoice.CardCode, invoice.CardName), i.Cfop ?? "", i.UsageName ?? "", i.GrandTotal, i.ItemName,
                UnlinkedEntry: kind == InventoryMovementKind.Entry && i.PurchaseInvoiceItemOriginKey == null && IsRemittanceCfop(i.Cfop)));
    }

    /// <summary>CFOP 1.9xx/2.9xx (remessas e retornos): sem origem, o preço da nota não é custo confiável.</summary>
    private static bool IsRemittanceCfop(string? cfop) =>
        cfop?.Trim() is { } c && (c.StartsWith("19") || c.StartsWith("29"));

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
