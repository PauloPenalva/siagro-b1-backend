using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class StorageCustodyByPeriodControllerTests
{
    private static StorageCustodyByPeriodController Controller(string erp) =>
        new(new StorageCustodyByPeriodReportService(TestDb.CreateUnitOfWork(), new RecordingFastReportService()),
            TestConfiguration.Erp(erp))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static StorageCustodyByPeriodRequest Request() =>
        new() { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31) };

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var result = await Controller("SAPB1").Report(Request());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Disponível somente no modo STANDALONE.", bad.Value);
    }

    [Fact]
    public async Task Refuses_an_inverted_period()
    {
        var request = Request();
        request.ToDate = new DateTime(2026, 9, 30);

        var result = await Controller("STANDALONE").Report(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A data final não pode ser anterior à inicial.", bad.Value);
    }

    [Fact]
    public async Task Returns_the_pdf_inline()
    {
        var controller = Controller("STANDALONE");

        var result = await controller.Report(Request());

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("inline; filename=\"storage-custody-by-period.pdf\"",
            controller.Response.Headers.ContentDisposition.ToString());
    }
}
