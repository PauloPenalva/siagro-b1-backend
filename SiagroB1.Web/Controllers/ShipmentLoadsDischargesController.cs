using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Tickets de descarga da carga: somente leitura aqui. A escrita passa pelas actions
/// ShipmentLoadsDischarge{Create,Update,Delete}, que carimbam o autor, recalculam as somas e
/// gravam o log na mesma transação.
/// </summary>
/// <remarks>
/// ⚠️ As duas rotas são declaradas à mão. Navegação e late property só respondem onde a rota foi
/// escrita: sem isso o grid recebe 404 e a aba aparece vazia sem erro visível.
/// </remarks>
public class ShipmentLoadsDischargesController(
    ShipmentLoadDischargesGetService getService) : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/Discharges")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/Discharges")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadDischarge>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
