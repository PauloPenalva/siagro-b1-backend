namespace SiagroB1.Reports.Helpers;

/// <summary>
/// "Hoje" do painel em horário de Brasília. A VPS roda em UTC: com <c>DateTime.Today</c>, depois
/// das 21:00 os cartões "de hoje" já mostrariam amanhã e as cargas do dia sumiriam de "Hoje"
/// (mesma armadilha do toISOString no frontend).
/// </summary>
public static class DashboardClock
{
    private static readonly TimeZoneInfo Brazil = FindBrazil();

    public static DateTime Today() => TodayAt(DateTime.UtcNow);

    public static DateTime TodayAt(DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Brazil).Date;

    private static TimeZoneInfo FindBrazil()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
    }
}
