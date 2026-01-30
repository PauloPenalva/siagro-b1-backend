using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.SalesContracts;

namespace SiagroB1.Web.Functions.SalesContracts;

public class SalesContractsGetAllocationsByContractController(
    SalesContractsGetAllocationsByContractService service
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/SalesContractsGetAllocationsByContract(SalesContractKey={key})")]
    public async Task<ActionResult<ICollection<SalesContractsGetAllocationsByContractDto>>> GetAsync([FromRoute] Guid key)
    {
        var invoices = service.ExecuteAsync(key);
        return Ok(new { invoices });
    }
}