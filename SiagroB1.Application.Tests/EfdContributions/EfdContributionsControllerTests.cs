using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Controllers;
using static SiagroB1.Application.Tests.EfdContributions.EfdContributionsFileServiceTests;

namespace SiagroB1.Application.Tests.EfdContributions;

public class EfdContributionsControllerTests
{
    [Fact]
    public async Task Controller_NonStandaloneReturnsBadRequest()
    {
        var controller = new EfdContributionsController(Service(TestDb.CreateUnitOfWork()), TestConfiguration.Erp("SAPB1"));

        var generate = Assert.IsType<BadRequestObjectResult>(await controller.Generate(Request()));
        var check = Assert.IsType<BadRequestObjectResult>(await controller.Check(Request()));

        Assert.Equal("Disponível somente no modo STANDALONE.", generate.Value);
        Assert.Equal("Disponível somente no modo STANDALONE.", check.Value);
    }

    [Fact]
    public async Task Controller_GeneratesLatin1TextFile()
    {
        var controller = new EfdContributionsController(Service(await Seeded()), TestConfiguration.Erp("STANDALONE"));

        var file = Assert.IsType<FileContentResult>(await controller.Generate(Request()));

        Assert.Equal("text/plain; charset=iso-8859-1", file.ContentType);
        Assert.Equal("EFD_CONTRIBUICOES_12345678000195_202607.txt", file.FileDownloadName);
    }

    [Fact]
    public async Task Controller_BlockingIssuesReturnBadRequest()
    {
        var controller = new EfdContributionsController(Service(await Seeded(settings: false)), TestConfiguration.Erp("STANDALONE"));

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.Generate(Request()));

        Assert.Contains("Parâmetros do SPED Fiscal não cadastrados", (string)bad.Value!);
    }

    [Fact]
    public async Task Controller_CheckReturnsTotals()
    {
        var controller = new EfdContributionsController(Service(await Seeded()), TestConfiguration.Erp("STANDALONE"));

        var ok = Assert.IsType<OkObjectResult>(await controller.Check(Request()));

        Assert.IsType<SiagroB1.Reports.Dtos.EfdContributionsCheckDto>(ok.Value);
    }
}
