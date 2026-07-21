using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Functions.SalesContracts;

public class SalesContractsGetShipmentReleasesAvailableController(
    SalesContractsGetShipmentReleasesAvailableService service
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/SalesContractsGetShipmentReleasesAvailable")]
    public ActionResult<IEnumerable<SalesContract>> Query()
    {
        return Ok(service.Query());
    }
}
