using SiagroB1.Infra.FiscalStock;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FiscalKardexControllerTests
{
    private static FiscalKardexController Controller(string erp) =>
        new(new FiscalKardexService(new FiscalStockLedgerLoader(TestDb.CreateUnitOfWork())), TestConfiguration.Erp(erp));

    private static FiscalKardexRequest Request(string branch = "01", int fromYear = 2026, int toYear = 2026, int toMonth = 10) => new()
    {
        BranchCode = branch,
        FromDate = new DateTime(fromYear, 10, 1),
        ToDate = new DateTime(toYear, toMonth, 30),
    };

    [Fact]
    public async Task Post_RefusesInSapb1Mode()
    {
        var result = await Controller("SAPB1").Get(Request());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Disponível somente no modo STANDALONE.", bad.Value);
    }

    [Theory]
    [InlineData("", 2026, 2026, 10)]
    [InlineData("  ", 2026, 2026, 10)]
    [InlineData("01", 2026, 2026, 9)]
    [InlineData("01", 1999, 2026, 10)]
    public async Task Post_RejectsInvalidRequest(string branch, int fromYear, int toYear, int toMonth)
    {
        var result = await Controller("STANDALONE").Get(Request(branch, fromYear, toYear, toMonth));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Informe a filial e um período válido.", bad.Value);
    }

    [Fact]
    public async Task Post_ReturnsDataInStandalone()
    {
        var result = await Controller("STANDALONE").Get(Request());

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<FiscalKardexData>(ok.Value);
    }
}
