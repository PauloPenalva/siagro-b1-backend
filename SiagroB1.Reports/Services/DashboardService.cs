using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Monta o painel da home (spec 2026-10-16-home-dashboard-design) numa chamada: cada seção é
/// uma consulta agregada no SQL, sem laço por produto ou armazém — o Reports da VPS não tem
/// memória para materializar coleções inteiras.
/// </summary>
public class DashboardService(IUnitOfWork db, ContractMonthlyPositionReportService monthlyPosition)
{
    /// <param name="today">Data do servidor na chamada; os testes fixam a data.</param>
    public async Task<DashboardDto> BuildAsync(DateTime today)
    {
        today = today.Date;

        // Posições (status Aprovado, todos os produtos/filiais) carregadas UMA vez e compartilhadas
        // pelos cartões de vencidos, pelas filas e pelo estoque.
        var request = new ContractMonthlyPositionRequest();
        var positions = await ContractPositionData.PurchasePositionsAsync(
            db.Context, ContractPositionData.PurchaseQuery(db.Context, request));
        positions.AddRange(await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, request)));

        return new DashboardDto
        {
            GeneratedAt = today,
            Financial = await FinancialAsync(db.Context, today),
            OverdueContracts = Overdue(positions, today),
            LoadsInProgress = await LoadsInProgressAsync(db.Context),
            Stock = await StockAsync(db.Context, positions),
            MonthlyPosition = await monthlyPosition.BuildRowsAsync(request, today),
            RecentLoads = await RecentLoadsAsync(db.Context, today),
            SalesToDeliver = Queue(positions.Where(p => p.Side == ContractPositionSide.Sales), today),
            PurchasesToReceive = Queue(positions.Where(p => p.Side == ContractPositionSide.Purchase), today),
        };
    }

    private const int QueueSize = 10;

    /// <summary>
    /// Físico por item + UM com a MESMA fórmula de <c>StorageTransactionsWarehouseBalanceService</c>
    /// (status Confirmado/Faturado, sete tipos, sinal por tipo), somando todos os armazéns numa
    /// consulta agrupada. "Em carga" = embarcado e ainda não faturado: já saiu do físico, mas a
    /// alocação na venda só nasce no faturamento — sem a coluna o líquido descontaria duas vezes.
    /// </summary>
    private static async Task<List<DashboardStockRowDto>> StockAsync(AppDbContext context, List<ContractPosition> positions)
    {
        var physical = await context.StorageTransactions
            .AsNoTracking()
            .Where(x => (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         x.TransactionStatus == StorageTransactionsStatus.Invoiced) &&
                        (x.TransactionType == StorageTransactionType.Purchase ||
                         x.TransactionType == StorageTransactionType.PurchaseReturn ||
                         x.TransactionType == StorageTransactionType.SalesShipment ||
                         x.TransactionType == StorageTransactionType.SalesShipmentReturn ||
                         x.TransactionType == StorageTransactionType.TransshipmentReceipt ||
                         x.TransactionType == StorageTransactionType.WarehouseLoss ||
                         x.TransactionType == StorageTransactionType.WarehouseGain))
            .GroupBy(x => new { x.ItemCode, x.UnitOfMeasureCode })
            .Select(g => new
            {
                g.Key.ItemCode,
                Uom = g.Key.UnitOfMeasureCode,
                ItemName = g.Max(x => x.ItemName),
                Quantity = g.Sum(x => (x.TransactionType == StorageTransactionType.Purchase ||
                                       x.TransactionType == StorageTransactionType.SalesShipmentReturn ||
                                       x.TransactionType == StorageTransactionType.TransshipmentReceipt ||
                                       x.TransactionType == StorageTransactionType.WarehouseGain)
                    ? x.NetWeight
                    : -x.NetWeight),
            })
            .ToListAsync();

        var inLoad = await context.ShipmentLoads
            .AsNoTracking()
            .Where(l => l.Status == ShipmentLoadStatus.Open || l.Status == ShipmentLoadStatus.PartiallyInvoiced)
            .GroupBy(l => new { l.ItemCode, l.UnitOfMeasureCode })
            .Select(g => new
            {
                g.Key.ItemCode,
                Uom = g.Key.UnitOfMeasureCode,
                ItemName = g.Max(l => l.ItemName),
                Quantity = g.Sum(l => l.TotalQuantity - l.InvoicedQuantity),
            })
            .ToListAsync();

        var rows = new Dictionary<(string Item, string Uom), DashboardStockRowDto>();

        DashboardStockRowDto Row(string item, string? uom, string? name)
        {
            var key = (item, Uom(uom));
            if (!rows.TryGetValue(key, out var row))
                rows[key] = row = new DashboardStockRowDto { ItemCode = item, UnitOfMeasure = key.Item2 };
            if (string.IsNullOrWhiteSpace(row.ItemName) && !string.IsNullOrWhiteSpace(name))
                row.ItemName = name;
            return row;
        }

        foreach (var p in physical)
            Row(p.ItemCode, p.Uom, p.ItemName).Physical = p.Quantity;
        foreach (var l in inLoad)
            Row(l.ItemCode, l.Uom, l.ItemName).InLoad = l.Quantity;
        foreach (var p in positions.Where(p => p.ToDeliver > 0m))
        {
            var row = Row(p.ItemCode, p.UnitOfMeasureCode, p.ItemName);
            if (p.Side == ContractPositionSide.Purchase) row.PurchaseToDeliver += p.ToDeliver;
            else row.SalesToDeliver += p.ToDeliver;
        }

        foreach (var row in rows.Values)
            row.Net = row.Physical is { } physicalQty
                ? physicalQty + row.InLoad + row.PurchaseToDeliver - row.SalesToDeliver
                : null;

        return rows.Values
            .Where(r => r.Physical is not null || r.InLoad != 0m || r.PurchaseToDeliver != 0m || r.SalesToDeliver != 0m)
            .OrderBy(r => r.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.ItemCode, StringComparer.Ordinal)
            .ThenBy(r => r.UnitOfMeasure, StringComparer.Ordinal)
            .ToList();
    }
    private const int RecentDays = 7;

    /// <summary>
    /// Planejada, em carregamento (Open + PartiallyInvoiced), em transbordo, em trânsito (Invoiced:
    /// faturada e ainda não descarregada) e recusa pendente. Descarregada, concluída, devolvida e
    /// cancelada já chegaram ou morreram — não são "em andamento".
    /// </summary>
    private static async Task<DashboardLoadsInProgressDto> LoadsInProgressAsync(AppDbContext context)
    {
        var counts = await context.ShipmentLoads
            .AsNoTracking()
            .Where(l => l.Status == ShipmentLoadStatus.Planned
                        || l.Status == ShipmentLoadStatus.Open
                        || l.Status == ShipmentLoadStatus.PartiallyInvoiced
                        || l.Status == ShipmentLoadStatus.InTransshipment
                        || l.Status == ShipmentLoadStatus.Invoiced
                        || l.Status == ShipmentLoadStatus.RefusalPending)
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Of(params ShipmentLoadStatus[] statuses) => counts.Where(c => statuses.Contains(c.Status)).Sum(c => c.Count);

        return new DashboardLoadsInProgressDto
        {
            Total = counts.Sum(c => c.Count),
            Planned = Of(ShipmentLoadStatus.Planned),
            Loading = Of(ShipmentLoadStatus.Open, ShipmentLoadStatus.PartiallyInvoiced),
            InTransshipment = Of(ShipmentLoadStatus.InTransshipment),
            InTransit = Of(ShipmentLoadStatus.Invoiced),
            RefusalPending = Of(ShipmentLoadStatus.RefusalPending),
        };
    }

    /// <summary>
    /// Últimos 7 dias pela data da carga, hoje primeiro, dia vazio com zero. Planejada fica de
    /// fora: sem romaneio nada foi carregado — o usuário pediu "carregamentos", não cargas abertas.
    /// </summary>
    private static async Task<List<DashboardDayLoadsDto>> RecentLoadsAsync(AppDbContext context, DateTime today)
    {
        var from = today.AddDays(-(RecentDays - 1));
        var to = today.AddDays(1);

        var rows = await context.ShipmentLoads
            .AsNoTracking()
            .Where(l => l.LoadDate >= from && l.LoadDate < to
                        && l.Status != ShipmentLoadStatus.Planned
                        && l.Status != ShipmentLoadStatus.Cancelled)
            .GroupBy(l => new { Date = l.LoadDate.Date, Uom = l.UnitOfMeasureCode })
            .Select(g => new { g.Key.Date, g.Key.Uom, Count = g.Count(), Quantity = g.Sum(l => l.TotalQuantity) })
            .ToListAsync();

        return Enumerable.Range(0, RecentDays)
            .Select(i => today.AddDays(-i))
            .Select(date =>
            {
                var ofDay = rows.Where(r => r.Date == date).ToList();
                return new DashboardDayLoadsDto
                {
                    Date = date,
                    Count = ofDay.Sum(r => r.Count),
                    Quantities = ofDay
                        .GroupBy(r => Uom(r.Uom))
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => new DashboardQuantityDto { UnitOfMeasure = g.Key, Quantity = g.Sum(r => r.Quantity) })
                        .ToList(),
                };
            })
            .ToList();
    }

    /// <summary>"KG" e "kg " são a mesma UM: o snapshot do contrato pode vir com caixa/espaço diferente.</summary>
    private static string Uom(string? code) => (code ?? "").Trim().ToUpperInvariant();

    private static bool IsOverdue(ContractPosition p, DateTime today) =>
        ContractPositionText.HasDeadline(p.DeliveryEndDate) && p.DeliveryEndDate.Date < today && p.ToDeliver > 0m;

    private static DashboardOverdueContractsDto Overdue(List<ContractPosition> positions, DateTime today)
    {
        DashboardOverdueSideDto Side(ContractPositionSide side)
        {
            var overdue = positions.Where(p => p.Side == side && IsOverdue(p, today)).ToList();
            return new DashboardOverdueSideDto
            {
                Count = overdue.Count,
                Quantities = overdue
                    .GroupBy(p => Uom(p.UnitOfMeasureCode))
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new DashboardQuantityDto { UnitOfMeasure = g.Key, Quantity = g.Sum(p => p.ToDeliver) })
                    .ToList(),
            };
        }

        return new DashboardOverdueContractsDto
        {
            Purchase = Side(ContractPositionSide.Purchase),
            Sales = Side(ContractPositionSide.Sales),
        };
    }

    /// <summary>Fila de um lado: saldo a entregar > 0, na ordem das seções do relatório de posição.</summary>
    private static DashboardContractQueueDto Queue(IEnumerable<ContractPosition> side, DateTime today)
    {
        var open = ContractPositionText.InSectionOrder(side.Where(p => p.ToDeliver > 0m)).ToList();

        return new DashboardContractQueueDto
        {
            Total = open.Count,
            Items = open.Take(QueueSize).Select(p => new DashboardContractQueueItemDto
            {
                Key = p.Key,
                Code = p.Code,
                CardCode = p.CardCode,
                CardName = p.CardName,
                ItemCode = p.ItemCode,
                ItemName = p.ItemName,
                HarvestSeasonCode = p.HarvestSeasonCode,
                ToDeliver = p.ToDeliver,
                UnitOfMeasure = Uom(p.UnitOfMeasureCode),
                CashFlowDate = p.StandardCashFlowDate,
                DeliveryEndDate = ContractPositionText.HasDeadline(p.DeliveryEndDate) ? p.DeliveryEndDate : null,
                IsOverdue = IsOverdue(p, today),
            }).ToList(),
        };
    }

    /// <summary>
    /// Mesmo universo das telas Contas a Pagar / a Receber (direção e Nature ≠ Advance), restrito a
    /// aberto/parcial. O valor é o saldo em aberto de TODO título, inclusive provisório sem nota —
    /// decisão do usuário: o cartão é o compromisso do contrato, não o liberável para baixa.
    /// </summary>
    private static async Task<DashboardFinancialDto> FinancialAsync(AppDbContext context, DateTime today)
    {
        var tomorrow = today.AddDays(1);

        var buckets = await context.FinancialDocuments
            .AsNoTracking()
            .Where(d => (d.Status == FinancialDocumentStatus.Open || d.Status == FinancialDocumentStatus.PartiallySettled)
                        && d.Nature != FinancialDocumentNature.Advance
                        && d.DueDate < tomorrow)
            .GroupBy(d => new { d.Direction, IsToday = d.DueDate >= today })
            .Select(g => new
            {
                g.Key.Direction,
                g.Key.IsToday,
                Count = g.Count(),
                Amount = g.Where(d => d.Currency == CurrencyType.Brl).Sum(d => d.NetAmount - d.SettledAmount),
            })
            .ToListAsync();

        DashboardMoneyDto Pick(FinancialDirection direction, bool isToday)
        {
            var b = buckets.FirstOrDefault(x => x.Direction == direction && x.IsToday == isToday);
            return new DashboardMoneyDto { Amount = b?.Amount ?? 0m, Count = b?.Count ?? 0 };
        }

        return new DashboardFinancialDto
        {
            PayablesDueToday = Pick(FinancialDirection.Payable, true),
            PayablesOverdue = Pick(FinancialDirection.Payable, false),
            ReceivablesDueToday = Pick(FinancialDirection.Receivable, true),
            ReceivablesOverdue = Pick(FinancialDirection.Receivable, false),
        };
    }
}
