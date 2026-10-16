using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>"Hoje" nestes testes é <see cref="ContractPositionSeed.Today"/> = 08/10/2026.</summary>
public class DashboardServiceTests
{
    [Fact]
    public async Task Build_EmptyDatabase_ReturnsEmptySectionsAndSevenDays()
    {
        var db = TestDb.CreateUnitOfWork();

        var dto = await Service(db).BuildAsync(Today);

        Assert.Equal(Today, dto.GeneratedAt.Date);
        Assert.Equal(0, dto.Financial.PayablesDueToday.Count);
        Assert.Empty(dto.Stock);
        Assert.Empty(dto.MonthlyPosition);
        Assert.Equal(7, dto.RecentLoads.Count);
        Assert.Equal(0, dto.SalesToDeliver.Total);
    }

    private static DashboardService Service(IUnitOfWork db) =>
        new(db, new ContractMonthlyPositionReportService(db, new RecordingFastReportService()));

    private static Task Save(IUnitOfWork db) => db.Context.SaveChangesAsync();
}
