using FastReport;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FastReportServiceUomTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplySingleUom_HidesTheQuantityTotalsOrTheNote(bool singleUom)
    {
        using var report = new Report();
        var page = new ReportPage { Name = "Page1" };
        report.Pages.Add(page);
        var band = new DataBand { Name = "Data1" };
        page.Bands.Add(band);
        var quantityTotal = new TextObject { Name = "uomSumTotalQuantity" };
        var note = new TextObject { Name = "uomMixedNote" };
        var freightTotal = new TextObject { Name = "txtSumFreightValue" };
        band.Objects.Add(quantityTotal);
        band.Objects.Add(note);
        band.Objects.Add(freightTotal);

        FastReportService.ApplySingleUom(report, singleUom);

        Assert.Equal(singleUom, quantityTotal.Visible);
        Assert.Equal(!singleUom, note.Visible);
        Assert.True(freightTotal.Visible);
    }
}
