using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.ShipmentReleases;

public class ShipmentReleasesGetPurchaseContractsController(ShipmentReleasesPurchaseContractsService service)
    : ODataController
{
    // Duas formas declaradas: WarehouseCode é opcional no EDM (troca de liberação — GAC-1177 v2 —
    // pesquisa o destino em QUALQUER armazém), e a chamada sem armazém OMITE o segmento.
    [EnableQuery]
    [HttpGet("odata/ShipmentReleasesGetPurchaseContracts(ItemCode={itemCode})")]
    [HttpGet("odata/ShipmentReleasesGetPurchaseContracts(ItemCode={itemCode},WarehouseCode={warehouseCode})")]
    public async Task<ActionResult<ICollection<ShipmentRelesesPurchaseContractsResponseDto>>> GetAsync(
        [FromRoute] string itemCode,
        [FromRoute] string? warehouseCode = null)
    {
        return Ok(await service.ExecuteAsync(itemCode, warehouseCode));
    }
}