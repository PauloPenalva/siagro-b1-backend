using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.SalesShipmentReleases;

public class SalesShipmentReleasesGetAvailableController(
    SalesShipmentReleasesGetAvailableService service)
    : ODataController
{
    /// <summary>
    /// Forma de UM parâmetro, preservada: rota de função é declarada à mão neste projeto, e a
    /// forma não declarada toma 404. Chamadas existentes continuam válidas.
    /// </summary>
    [EnableQuery]
    [HttpGet("odata/SalesShipmentReleasesGetAvailable(ItemCode={itemCode})")]
    public ActionResult<IEnumerable<SalesShipmentReleaseAvailableDto>> Get([FromRoute] string itemCode)
    {
        return Ok(service.Query(itemCode));
    }

    [EnableQuery]
    [HttpGet("odata/SalesShipmentReleasesGetAvailable(ItemCode={itemCode},IncludeContractsWithoutBalance={includeContractsWithoutBalance})")]
    public ActionResult<IEnumerable<SalesShipmentReleaseAvailableDto>> Get(
        [FromRoute] string itemCode,
        [FromRoute] bool includeContractsWithoutBalance)
    {
        return Ok(service.Query(itemCode, includeContractsWithoutBalance));
    }
}
