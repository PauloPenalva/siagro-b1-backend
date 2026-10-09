using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>Devoluções da recusa aguardando NF-e (spec 2026-10-09 §7). Vazia sem recusa pendente.</summary>
public class ShipmentLoadsGetPendingRefusalController(ShipmentLoadsPendingRefusalService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [EnableQuery]
    [HttpGet("odata/ShipmentLoadsGetPendingRefusal(Key={key})")]
    public async Task<ActionResult<IEnumerable<ShipmentLoadPendingRefusalReturnDto>>> Get([FromRoute] Guid key) =>
        Ok(await service.ExecuteAsync(key));
}
