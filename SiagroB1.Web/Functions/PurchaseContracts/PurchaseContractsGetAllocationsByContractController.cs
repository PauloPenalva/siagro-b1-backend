using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsGetAllocationsByContractController(
    PurchaseContractsGetAllocationsByContractService service
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/PurchaseContractsGetAllocationsByContract(PurchaseContractKey={key})")]
    public async Task<ActionResult<ICollection<PurchaseContractAllocationsByContractDto>>> GetAsync([FromRoute] Guid key)
    {
        var allocations = await service.ExecuteAsync(key);
        return Ok(new { allocations });
    }
}