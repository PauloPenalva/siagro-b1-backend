using SiagroB1.Reports.Helpers;

namespace SiagroB1.Application.Tests.Reports;

public class ReportTextTests
{
    private static readonly DateTime Jul01 = new(2026, 7, 1);
    private static readonly DateTime Jul31 = new(2026, 7, 31);

    [Fact]
    public void ValidatePeriod_RequiresBothDates()
    {
        Assert.Equal("Informe o período.", ReportText.ValidatePeriod(default, Jul31, "Informe o período."));
        Assert.Equal("Informe o período.", ReportText.ValidatePeriod(Jul01, default, "Informe o período."));
    }

    [Fact]
    public void ValidatePeriod_RejectsAnInvertedPeriod() =>
        Assert.Equal("A data final não pode ser anterior à inicial.", ReportText.ValidatePeriod(Jul31, Jul01, "x"));

    [Fact]
    public void ValidatePeriod_AcceptsASingleDay() =>
        Assert.Null(ReportText.ValidatePeriod(Jul01, Jul01, "x"));

    [Fact]
    public void Period_FormatsBothEnds() =>
        Assert.Equal("Data da carga: 01/07/2026 a 31/07/2026", ReportText.Period("Data da carga", Jul01, Jul31));

    [Theory]
    [InlineData("ARM01", "ARMAZÉM CENTRAL", "ARMAZÉM CENTRAL")]
    [InlineData("ARM01", null, "ARM01")]
    [InlineData("ARM01", " ", "ARM01")]
    [InlineData(null, null, "")]
    public void NameOrCode_PrefersTheName(string? code, string? name, string expected) =>
        Assert.Equal(expected, ReportText.NameOrCode(code, name));

    [Fact]
    public void JoinDistinct_SkipsBlanksAndDuplicatesInOrdinalOrder() =>
        Assert.Equal(
            "AGRO NORTE, COOPERATIVA CENTRAL",
            ReportText.JoinDistinct(["COOPERATIVA CENTRAL", "AGRO NORTE", "COOPERATIVA CENTRAL ", " ", null]));

    [Fact]
    public void IsSingleUnit_IgnoresCaseAndTreatsEmptyAsSingle()
    {
        Assert.True(ReportText.IsSingleUnit([]));
        Assert.True(ReportText.IsSingleUnit(["KG", "kg", "KG "]));
        Assert.False(ReportText.IsSingleUnit(["KG", "TN"]));
    }

    [Theory]
    [InlineData("SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001)")]
    [InlineData("SOJA EM GRÃOS (10001)", "SOJA EM GRÃOS (10001)")]
    [InlineData(null, null)]
    public void ProductOfGroup_DropsTheUnitSuffix(string? group, string? expected) =>
        Assert.Equal(expected, ReportText.ProductOfGroup(group));

    [Fact]
    public void InvoiceReportText_DelegatesWithTheSameStrings()
    {
        Assert.Equal("(C001) COOPERATIVA", InvoiceReportText.Partner("C001", "COOPERATIVA"));
        Assert.Equal("SOJA (10001)", InvoiceReportText.Product("10001", "SOJA"));
        Assert.Equal(ReportText.NoProduct, InvoiceReportText.NoProduct);
        Assert.Equal("123/1", InvoiceReportText.DocumentNumber("123", "1"));
        Assert.Equal("01/07/2026", InvoiceReportText.Date(Jul01));
    }
}
