using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FiscalInventoryControllerTests
{
    private static FiscalInventoryController Controller(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        return new(new FiscalInventoryService(db, new FiscalKardexService(db)), TestConfiguration.Erp(erp));
    }

    private static FiscalInventoryRequest Request(string branch = "01", int year = 2026) => new()
    {
        BranchCode = branch,
        Date = new DateTime(year, 10, 20),
    };

    [Fact]
    public async Task Post_RefusesInSapb1Mode()
    {
        var bad = Assert.IsType<BadRequestObjectResult>(await Controller("SAPB1").Get(Request()));
        Assert.Equal("Disponível somente no modo STANDALONE.", bad.Value);
    }

    [Theory]
    [InlineData("", 2026)]
    [InlineData("  ", 2026)]
    [InlineData("01", 1999)]
    public async Task Post_RejectsInvalidRequest(string branch, int year)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(await Controller("STANDALONE").Get(Request(branch, year)));
        Assert.Equal("Informe a filial e a data do inventário.", bad.Value);
    }

    [Fact]
    public async Task Post_ReturnsDataInStandalone()
    {
        var ok = Assert.IsType<OkObjectResult>(await Controller("STANDALONE").Get(Request()));
        Assert.IsType<FiscalInventoryData>(ok.Value);
    }
}
