using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class SalesContractsAllocationsController(
    SalesContractsAllocationGetService allocationGetService
    )
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<SalesContractAllocation>> Get()
    {
        return Ok(allocationGetService.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<SalesContractAllocation>> Get([FromRoute] Guid key)
    {
        var item = await allocationGetService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

}
