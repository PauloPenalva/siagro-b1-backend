using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class NotificationGroupSubscriptionsController(NotificationGroupSubscriptionsService service)
    : ODataBaseController<NotificationGroupSubscription, Guid>(service)
{
    /// <summary>
    /// Edição parcial da linha. Mesmo motivo de
    /// <see cref="NotificationGroupMembersController.Patch"/>: o <c>Put</c> da base zera a FK
    /// num PATCH parcial, que é o que a tela envia ao trocar o evento assinado.
    /// </summary>
    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch(
        [FromRoute] Guid key, [FromBody] Delta<NotificationGroupSubscription> patch)
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
