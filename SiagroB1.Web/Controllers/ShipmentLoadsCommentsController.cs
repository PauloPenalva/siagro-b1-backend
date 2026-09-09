using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Comentários da carga: somente leitura aqui. A escrita passa pelas actions
/// ShipmentLoadsComment{Create,Update,Delete}, que carimbam o autor e gravam o log de alterações
/// na mesma transação.
/// </summary>
public class ShipmentLoadsCommentsController(
    ShipmentLoadsCommentsGetService getService)
    : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/CommentEntries")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/CommentEntries")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadComment>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
