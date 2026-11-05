using System.Security;

namespace SiagroB1.Commons.Time;

/// <summary>
/// Fuso de Brasília em um único lugar (feed da CMA, fechamento diário, agendamento do Hangfire).
/// Nunca lança: Linux sem tzdata / InvariantGlobalization cai no UTC-3 fixo.
/// </summary>
public static class SaoPauloTime
{
    private static readonly Lazy<TimeZoneInfo> Lazy = new(() => Resolve("America/Sao_Paulo", "E. South America Standard Time"));

    public static TimeZoneInfo Zone => Lazy.Value;

    /// <summary>Data de hoje no horário de Brasília, segundo o relógio informado.</summary>
    public static DateOnly Today(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);

    /// <summary>
    /// Resolve o fuso sem nunca lançar: id IANA, id do Windows e, por fim, UTC-3 fixo
    /// (o Brasil não tem horário de verão desde 2019).
    /// </summary>
    internal static TimeZoneInfo Resolve(params string[] ids)
    {
        foreach (var id in ids)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException
                                           or SecurityException or ArgumentException)
            {
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone("America/Sao_Paulo", TimeSpan.FromHours(-3), "Sao Paulo (UTC-3)", "Sao Paulo (UTC-3)");
    }
}
