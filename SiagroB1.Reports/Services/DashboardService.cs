using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

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

        return new DashboardDto
        {
            GeneratedAt = today,
            Financial = await FinancialAsync(db.Context, today),
            RecentLoads = Enumerable.Range(0, 7)
                .Select(i => new DashboardDayLoadsDto { Date = today.AddDays(-i) })
                .ToList(),
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
