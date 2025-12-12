using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsAllocationsController(
    PurchaseContractsAllocationGetService allocationGetService
    ) 
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractAllocation>> Get()
    {
        return Ok(allocationGetService.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<PurchaseContractAllocation>> Get([FromRoute] Guid key)
    {
        var item = await allocationGetService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
}