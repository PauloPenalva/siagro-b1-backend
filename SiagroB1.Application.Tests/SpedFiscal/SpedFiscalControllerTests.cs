using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Controllers;
using static SiagroB1.Application.Tests.SpedFiscal.SpedFiscalFileServiceTests;

namespace SiagroB1.Application.Tests.SpedFiscal;

public class SpedFiscalControllerTests
{
    private static SpedFiscalController Controller(string erp) =>
        new(Service(TestDb.CreateUnitOfWork()), TestConfiguration.Erp(erp));

    [Fact]
    public async Task Controller_NonStandaloneReturnsBadRequest()
    {
        var generate = Assert.IsType<BadRequestObjectResult>(await Controller("SAPB1").Generate(Request()));
        var check = Assert.IsType<BadRequestObjectResult>(await Controller("SAPB1").Check(Request()));

        Assert.Equal("Disponível somente no modo STANDALONE.", generate.Value);
        Assert.Equal("Disponível somente no modo STANDALONE.", check.Value);
    }

    [Fact]
    public async Task Controller_InvalidMonthReturnsBadRequest()
    {
        var request = Request(2026, 13);

        var bad = Assert.IsType<BadRequestObjectResult>(await Controller("STANDALONE").Generate(request));

        Assert.Equal("Informe a filial e um mês válido.", bad.Value);
    }

    [Fact]
    public async Task Controller_GeneratesLatin1TextFile()
    {
        var db = await Seeded();
        var controller = new SpedFiscalController(Service(db), TestConfiguration.Erp("STANDALONE"));

        var file = Assert.IsType<FileContentResult>(await controller.Generate(Request()));

        Assert.Equal("text/plain; charset=iso-8859-1", file.ContentType);
        Assert.Equal("SPED_FISCAL_12345678000195_202607.txt", file.FileDownloadName);
    }

    [Fact]
    public async Task Controller_BlockingIssuesReturnBadRequest()
    {
        var db = await Seeded(settings: false);
        var controller = new SpedFiscalController(Service(db), TestConfiguration.Erp("STANDALONE"));

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.Generate(Request()));

        Assert.Contains("Parâmetros do SPED Fiscal não cadastrados", (string)bad.Value!);
    }
}
