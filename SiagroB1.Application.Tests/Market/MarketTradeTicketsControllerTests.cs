using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Web.Controllers;
using static SiagroB1.Application.Tests.Market.MarketTradeTicketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Erros REST da boleta saem em text/plain só com a mensagem pt-BR (não como erro OData).</summary>
public class MarketTradeTicketsControllerTests
{
    private static MarketTradeTicketsController Controller(SiagroB1.Infra.UnitOfWork db, params string[] granted) =>
        new(db.Context, TicketService(db, granted), new ManualTimeProvider(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "comprador")], "test")),
                },
            },
        };

    private static void AssertText(IActionResult result, int status, string contains)
    {
        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(status, content.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", content.ContentType);
        Assert.Contains(contains, content.Content);
    }

    [Fact]
    public async Task Create_BusinessError_PlainText400()
    {
        var db = await SeedTicketScenarioAsync();
        AssertText(await Controller(db).Create(Input(card: "X"), CancellationToken.None), 400, "Parceiro não encontrado.");
    }

    [Fact]
    public async Task Approve_Forbidden_PlainText403_And_Unknown_PlainText404()
    {
        var db = await SeedTicketScenarioAsync();
        var created = Assert.IsType<JsonResult>(await Controller(db).Create(Input(), CancellationToken.None));
        var id = ((SiagroB1.Domain.Entities.Market.MarketTradeTicket)created.Value!).Id;

        AssertText(await Controller(db).Approve(id, CancellationToken.None), 403, "permissão");
        AssertText(await Controller(db, PermissionCodes.MarketTicketApprove).Approve(999, CancellationToken.None), 404, "999");
    }
}
