using SiagroB1.Application.Services.Mdfe;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Teste de texto do comando de reserva do MDF-e: trava a linha da série e devolve o número ANTERIOR.</summary>
public class MdfeNumberReservationSqlTests
{
    [Fact]
    public void Reservation_sql_locks_branch_mdfe_series()
    {
        var sql = MdfeNumberReservationService.ReserveSql;

        Assert.Contains("BRANCH_MDFE_SERIES", sql);
        Assert.DoesNotContain("BRANCH_CTE_SERIES", sql);
        Assert.DoesNotContain("BRANCH_NFE_SERIES", sql);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql);
        Assert.Contains("OUTPUT deleted.NextNumber", sql);
        Assert.Contains("SET NextNumber = NextNumber + 1", sql);
    }
}
