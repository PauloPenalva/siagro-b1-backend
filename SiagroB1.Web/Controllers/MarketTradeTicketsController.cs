using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Commons.Time;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

public sealed record MarketTradeTicketRejectInput(string? Reason);

/// <summary>
/// Boletas do Mercado: leitura por OData (só GET) e as operações por REST em <c>market/tickets</c>.
/// Criar: qualquer autenticado; aprovar/rejeitar/cancelar: a permissão é checada no serviço (403 com texto pt-BR).
/// </summary>
[Authorize]
[MarketModuleEnabled]
public class MarketTradeTicketsController(AppDbContext context, MarketTradeTicketService service, TimeProvider time)
    : ODataController
{
    private string UserName => User.Identity?.Name ?? string.Empty;

    [EnableQuery(PageSize = 200)]
    public IActionResult Get() => Ok(context.MarketTradeTickets.AsNoTracking());

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] int key)
    {
        var ticket = await context.MarketTradeTickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == key);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("market/tickets")]
    public Task<IActionResult> Create([FromBody] MarketTradeTicketInput input, CancellationToken cancellationToken) =>
        Run(() => service.CreateAsync(input, UserName, Today(), cancellationToken));

    [HttpPost("market/tickets/{id:int}/approve")]
    public Task<IActionResult> Approve(int id, CancellationToken cancellationToken) =>
        Run(() => service.ApproveAsync(id, UserName, cancellationToken));

    [HttpPost("market/tickets/{id:int}/reject")]
    public Task<IActionResult> Reject(int id, [FromBody] MarketTradeTicketRejectInput? input, CancellationToken cancellationToken) =>
        Run(() => service.RejectAsync(id, input?.Reason ?? string.Empty, UserName, cancellationToken));

    [HttpPost("market/tickets/{id:int}/cancel")]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) =>
        Run(() => service.CancelAsync(id, UserName, cancellationToken));

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), SaoPauloTime.Zone).DateTime);

    private async Task<IActionResult> Run(Func<Task<MarketTradeTicket>> action)
    {
        try
        {
            var ticket = await action();
            // Carimbos gravados em UTC voltam do banco sem Kind: marca UTC para serializar com "Z".
            ticket.CreatedAt = Utc(ticket.CreatedAt)!.Value;
            ticket.ApprovedAt = Utc(ticket.ApprovedAt);
            ticket.BoardApprovedAt = Utc(ticket.BoardApprovedAt);
            ticket.RejectedAt = Utc(ticket.RejectedAt);
            ticket.CanceledAt = Utc(ticket.CanceledAt);
            ticket.QuotedAt = Utc(ticket.QuotedAt);
            return new JsonResult(ticket, MarketQuotesStreamLoop.JsonOptions);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ForbiddenException e)
        {
            return StatusCode(StatusCodes.Status403Forbidden, e.Message);
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static DateTime? Utc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
