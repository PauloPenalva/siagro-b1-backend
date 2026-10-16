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
            RecentLoads = Enumerable.Range(0, 7)
                .Select(i => new DashboardDayLoadsDto { Date = today.AddDays(-i) })
                .ToList(),
            SalesToDeliver = Queue(positions.Where(p => p.Side == ContractPositionSide.Sales), today),
            PurchasesToReceive = Queue(positions.Where(p => p.Side == ContractPositionSide.Purchase), today),
        };
    }

    private const int QueueSize = 10;

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
