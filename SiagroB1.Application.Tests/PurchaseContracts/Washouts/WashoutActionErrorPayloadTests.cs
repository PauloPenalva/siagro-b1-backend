using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Results;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Web.Actions.PurchaseContracts;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

/// <summary>
/// Verificação em browser de 14/09/2026: uma action de washout recusada por regra de negócio
/// devolvia a mensagem como <c>Edm.String</c> cru (<c>BadRequest(e.Message)</c> via
/// <c>ControllerBase</c>), e o modelo OData v4 do UI5 não consegue extrair mensagem nenhuma
/// desse formato — a tela só mostra "Communication error: 400 Bad Request". O fix troca o
/// payload por um erro OData de verdade (<see cref="BadRequestODataResult"/> /
/// <see cref="NotFoundODataResult"/>), que o UI5 sabe interpretar.
///
/// Passa pelo controller público (<see cref="PurchaseContractsWashoutRejectController"/>)
/// porque <c>WashoutActionResults</c> é internal e o projeto de teste não tem
/// InternalsVisibleTo.
/// </summary>
public class WashoutActionErrorPayloadTests
{
    private static PurchaseContractsWashoutRejectController BuildController()
    {
        var context = TestDb.CreateUnitOfWork().Context;

        var service = new PurchaseContractsWashoutRejectService(
            context,
            new PurchaseContractsWashedOutVolumeService(context),
            new PurchaseContractsChangeLogService(context),
            TestNotificationOutbox.For(context));

        return new PurchaseContractsWashoutRejectController(service)
        {
            // O controller lê User.Identity dentro do Post.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task Business_error_returns_an_odata_bad_request_with_the_message()
    {
        var controller = BuildController();

        // Motivo em branco: ApplicationException antes de qualquer busca no banco.
        var result = await controller.RejectAsync(new ODataActionParameters
        {
            ["Key"] = Guid.NewGuid(),
            ["Comments"] = "  ",
        });

        var badRequest = Assert.IsType<BadRequestODataResult>(result);
        Assert.Equal("Informe o motivo da rejeição.", badRequest.Error.Message);
    }

    [Fact]
    public async Task Unknown_key_returns_an_odata_not_found_with_the_message()
    {
        var controller = BuildController();

        var result = await controller.RejectAsync(new ODataActionParameters
        {
            ["Key"] = Guid.NewGuid(),
            ["Comments"] = "motivo",
        });

        var notFound = Assert.IsType<NotFoundODataResult>(result);
        Assert.Equal("Washout não encontrado.", notFound.Error.Message);
    }
}
