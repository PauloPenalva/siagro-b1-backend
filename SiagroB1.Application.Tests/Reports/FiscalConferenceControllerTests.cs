using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FiscalConferenceControllerTests
{
    private static FiscalConferenceController Controller(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var funrural = new FunruralWithholdingReportService(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));
        return new FiscalConferenceController(new FiscalConferenceService(db, funrural), TestConfiguration.Erp(erp));
    }

    private static FiscalConferenceRequest Request(int year = 2026, int month = 10) =>
        new() { Year = year, Month = month };

    [Fact]
    public async Task Post_RefusesInSapb1Mode()
    {
        var result = await Controller("SAPB1").Get(Request());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Disponível somente no modo STANDALONE.", bad.Value);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 10)]
    [InlineData(10000, 1)]
    public async Task Post_RejectsInvalidMonth(int year, int month)
    {
        var result = await Controller("STANDALONE").Get(Request(year, month));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Informe um mês válido.", bad.Value);
    }

    [Fact]
    public async Task Post_ReturnsDataInStandalone()
    {
        var result = await Controller("STANDALONE").Get(Request());

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<FiscalConferenceData>(ok.Value);
    }
}
