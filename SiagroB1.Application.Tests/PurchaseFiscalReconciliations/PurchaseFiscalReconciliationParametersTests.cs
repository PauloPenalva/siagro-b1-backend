using SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationParametersTests
{
    [Fact]
    public void Unquotes_and_turns_empty_into_null()
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter("'F0001'", "'SOJA'", "''", "'2026-10-01'", "''", "'F0002'", true);

        Assert.Null(error);
        Assert.Equal(("F0001", "SOJA", (string?)null, (DateTime?)new DateTime(2026, 10, 1), (DateTime?)null, (string?)"F0002", true),
            (filter!.CardCode, filter.ItemCode, filter.ContractCode, filter.DateFrom, filter.DateTo, filter.IssuerCardCode, filter.IncludeFullyLinked));
    }

    [Fact]
    public void Unescapes_doubled_quotes()
    {
        var (filter, _) = PurchaseFiscalReconciliationParameters.Filter("'D''AVILA'", "'SOJA'", null, null, null, null, false);

        Assert.Equal("D'AVILA", filter!.CardCode);
    }

    [Fact]
    public void Invalid_date_is_an_error()
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter("'F0001'", "'SOJA'", "''", "'01/10/2026'", "''", "''", false);

        Assert.Null(filter);
        Assert.Equal("Informe as datas no formato aaaa-mm-dd.", error);
    }
}
