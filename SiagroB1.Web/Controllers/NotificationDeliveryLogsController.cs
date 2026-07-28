using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Envios por destinatário: somente leitura.
/// </summary>
public class NotificationDeliveryLogsController(NotificationDeliveryLogsGetService getService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<NotificationDeliveryLog>> Get() => Ok(getService.QueryAll());
}
