using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>Código da perna de compra de cada Saída vinculada à carga (GAC-1175).</summary>
public class ShipmentLoadsGetPurchaseLegsController(ShipmentLoadsPurchaseLegsService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [EnableQuery]
    [HttpGet("odata/ShipmentLoadsGetPurchaseLegs(Key={key})")]
    public async Task<ActionResult<IEnumerable<ShipmentLoadPurchaseLegDto>>> Get([FromRoute] Guid key) =>
        Ok(await service.ExecuteAsync(key));
}
