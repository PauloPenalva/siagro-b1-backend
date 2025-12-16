using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsGetShipmentReleasesAvailableController(
    PurchaseContractsGetShipmentReleasesAvailableService service
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/PurchaseContractsGetShipmentReleasesAvailable")]
    public ActionResult<IEnumerable<PurchaseContract>> Query()
    {
        return Ok(service.Query());
    }
}