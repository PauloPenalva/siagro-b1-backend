using SiagroB1.Infra;
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
            RecentLoads = Enumerable.Range(0, 7)
                .Select(i => new DashboardDayLoadsDto { Date = today.AddDays(-i) })
                .ToList(),
        };
    }
}
