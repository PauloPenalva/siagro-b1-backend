using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class NotificationGroupSubscriptionsController(NotificationGroupSubscriptionsService service)
    : ODataBaseController<NotificationGroupSubscription, Guid>(service)
{
    /// <summary>
    /// Inclusão de evento assinado a partir do grupo já gravado. Mesmo motivo de
    /// <see cref="NotificationGroupMembersController.PostToMembers"/>: a aba "Eventos" também
    /// é ligada por navegação (<c>rows="{Subscriptions}"</c>), então o POST vai para
    /// <c>NotificationGroups(key)/Subscriptions</c> e sem esta rota devolve 405.
    /// </summary>
    [HttpPost("odata/NotificationGroups({key:guid})/Subscriptions")]
    [HttpPost("odata/NotificationGroups/{key:guid}/Subscriptions")]
    public async Task<IActionResult> PostToSubscriptions(
        [FromRoute] Guid key, [FromBody] NotificationGroupSubscription subscription)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        subscription.NotificationGroupKey = key;

        try
        {
            await service.CreateAsync(subscription);
        }
        catch (DefaultException exception)
        {
            return BadRequest(exception.Message);
        }

        return Created(subscription);
    }

    /// <summary>
    /// Leitura de UM evento assinado pelo caminho do grupo — a releitura que o modelo faz logo
    /// após criar a linha. Mesmo motivo de
    /// <see cref="NotificationGroupMembersController.GetFromMembers"/>.
    /// </summary>
    [HttpGet("odata/NotificationGroups({key:guid})/Subscriptions({subscriptionKey:guid})")]
    [HttpGet("odata/NotificationGroups/{key:guid}/Subscriptions/{subscriptionKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<NotificationGroupSubscription>> GetFromSubscriptions(
        [FromRoute] Guid key, [FromRoute] Guid subscriptionKey)
    {
        var subscription = await service.GetByIdAsync(subscriptionKey);

        return subscription is null || subscription.NotificationGroupKey != key
            ? NotFound()
            : Ok(subscription);
    }

    /// <summary>
    /// Exclusão do evento assinado pelo caminho do grupo. Mesmo motivo de
    /// <see cref="NotificationGroupMembersController.DeleteFromMembers"/>.
    /// </summary>
    [HttpDelete("odata/NotificationGroups({key:guid})/Subscriptions({subscriptionKey:guid})")]
    [HttpDelete("odata/NotificationGroups/{key:guid}/Subscriptions/{subscriptionKey:guid}")]
    public async Task<IActionResult> DeleteFromSubscriptions(
        [FromRoute] Guid key, [FromRoute] Guid subscriptionKey)
    {
        var subscription = await service.GetByIdAsync(subscriptionKey);

        if (subscription is null || subscription.NotificationGroupKey != key)
            return NotFound();

        await service.DeleteAsync(subscriptionKey);

        return NoContent();
    }

    /// <summary>
    /// Edição parcial da linha. Mesmo motivo de
    /// <see cref="NotificationGroupMembersController.Patch"/>: o <c>Put</c> da base zera a FK
    /// num PATCH parcial, que é o que a tela envia ao trocar o evento assinado — e precisa ser
    /// <c>override</c>, senão o roteamento continua caindo no <c>Patch</c> da base.
    /// </summary>
    [AcceptVerbs("PATCH", "MERGE")]
    public override async Task<IActionResult> Patch(
        [FromODataUri] Guid key, [FromBody] Delta<NotificationGroupSubscription> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var subscription = await service.GetByIdAsync(key);

        if (subscription is null)
            return NotFound();

        try
        {
            patch.Patch(subscription);
            await service.SaveTrackedChangesAsync();
        }
        catch (DefaultException exception)
        {
            return BadRequest(exception.Message);
        }

        return Updated(subscription);
    }
}
