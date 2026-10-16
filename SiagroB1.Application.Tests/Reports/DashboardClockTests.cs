using SiagroB1.Reports.Helpers;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>"Hoje" do painel é o dia em Brasília, não o dia do relógio do servidor (a VPS roda em UTC).</summary>
public class DashboardClockTests
{
    [Fact]
    public void TodayAt_EveningInBrazil_IsStillTheSameDay()
    {
        // 23:30Z = 20:30 em Brasília (UTC-3): ainda dia 16.
        Assert.Equal(new DateTime(2026, 10, 16), DashboardClock.TodayAt(new DateTime(2026, 10, 16, 23, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void TodayAt_AfterMidnightUtc_IsStillYesterdayInBrazil()
    {
        // 01:30Z do dia 17 = 22:30 do dia 16 em Brasília.
        Assert.Equal(new DateTime(2026, 10, 16), DashboardClock.TodayAt(new DateTime(2026, 10, 17, 1, 30, 0, DateTimeKind.Utc)));
    }
}
