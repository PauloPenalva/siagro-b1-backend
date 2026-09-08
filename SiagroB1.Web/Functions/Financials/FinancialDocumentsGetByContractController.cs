using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Financials;

public class FinancialDocumentsGetByContractController(
    FinancialDocumentsGetByContractService service) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/FinancialDocumentsGetByContract(ContractType={contractType},ContractKey={contractKey})")]
    public async Task<ActionResult<IEnumerable<FinancialDocumentByContractDto>>> Get(
        [FromRoute] string contractType, [FromRoute] Guid contractKey)
    {
        try
        {
            return Ok(await service.ExecuteAsync(contractType, contractKey));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
