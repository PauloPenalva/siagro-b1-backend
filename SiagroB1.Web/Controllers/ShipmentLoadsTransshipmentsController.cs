using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Transbordos da carga (GAC-1181): somente leitura aqui. A escrita passa pelas actions
/// ShipmentLoadsTransshipment{Start,RegisterEntry,Reverse}, que validam o negócio, recalculam o
/// quarto termo do saldo e gravam o log de movimentação na mesma transação.
/// </summary>
/// <remarks>
/// ⚠️ As duas rotas são declaradas à mão. Navegação e late property só respondem onde a rota foi
/// escrita: sem isso o grid recebe 404 e a aba aparece vazia sem erro visível.
/// </remarks>
public class ShipmentLoadsTransshipmentsController(
    ShipmentLoadsTransshipmentsGetService getService) : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/Transshipments")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/Transshipments")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadTransshipment>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
