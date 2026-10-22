using SiagroB1.Application.Services.Cte;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>
/// Teste de texto do comando de reserva (sem banco): duas confirmações simultâneas na mesma série
/// só recebem números diferentes se o UPDATE travar a linha e devolver o valor ANTERIOR.
/// </summary>
public class CteNumberReservationSqlTests
{
    [Fact]
    public void Reserve_sql_locks_the_cte_series_row_and_outputs_the_previous_number()
    {
        var sql = CteNumberReservationService.ReserveSql;

        Assert.Contains("BRANCH_CTE_SERIES", sql);
        Assert.DoesNotContain("BRANCH_NFE_SERIES", sql);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql);
        Assert.Contains("OUTPUT deleted.NextNumber", sql);
        Assert.Contains("SET NextNumber = NextNumber + 1", sql);
    }
}
