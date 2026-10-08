using FastReport;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FastReportServiceFiscalTests
{
    [Fact]
    public void HideFiscalObjects_HidesOnlyObjectsPrefixedWithFiscal()
    {
        using var report = new Report();
        var page = new ReportPage { Name = "Page1" };
        report.Pages.Add(page);
        var band = new DataBand { Name = "Data1" };
        page.Bands.Add(band);
        var icms = new TextObject { Name = "fiscalIcms" };
        var total = new TextObject { Name = "txtTotal" };
        band.Objects.Add(icms);
        band.Objects.Add(total);

        FastReportService.HideFiscalObjects(report);

        Assert.False(icms.Visible);
        Assert.True(total.Visible);
    }
}
