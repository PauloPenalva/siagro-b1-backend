using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;


/// <summary>
/// Log de notificações: somente leitura. As linhas nascem nos serviços de mutação do contrato,
/// na mesma transação da alteração — não há endpoint de escrita, de propósito. A única ação
/// disponível é o reenvio (<c>NotificationOutboxResend</c>).
/// </summary>
public class NotificationOutboxMessagesController(
    NotificationOutboxGetService getService,
    NotificationDeliveryLogsGetService deliveryLogsGetService)
    : ODataController
{
    /// <summary>
    /// Envios da notificação, um por destinatário e tentativa.
    ///
    /// Rota explícita pelo mesmo motivo de <c>NotificationGroupsController.GetMembers</c>: sem
    /// um <c>GetDeliveries</c> no controller, o roteamento por convenção do OData devolve 404
    /// para <c>NotificationOutboxMessages(key)/Deliveries</c> — a tela acusa
    /// "Communication error: 404" ao selecionar uma linha.
    /// </summary>
    [HttpGet("odata/NotificationOutboxMessages({key:guid})/Deliveries")]
    [HttpGet("odata/NotificationOutboxMessages/{key:guid}/Deliveries")]
    [EnableQuery]
    public ActionResult<IEnumerable<NotificationDeliveryLog>> GetDeliveries([FromRoute] Guid key) =>
        Ok(deliveryLogsGetService.QueryAll().Where(log => log.OutboxMessageKey == key));

    [EnableQuery(MaxExpansionDepth = 2)]
    public ActionResult<IEnumerable<NotificationOutboxMessage>> Get() => Ok(getService.QueryAll());

    [EnableQuery(MaxExpansionDepth = 2)]
    public async Task<ActionResult<NotificationOutboxMessage>> Get([FromRoute] Guid key)
    {
        var message = await getService.GetByIdAsync(key);

        return message is null ? NotFound() : Ok(message);
    }
}
