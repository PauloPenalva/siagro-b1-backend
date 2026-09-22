using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Dtos;
using SiagroB1.Web.Actions.ContractDrafts;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsListByContractController(ContractDraftsGetService service) : ODataController
{
    [HttpGet("odata/ContractDraftsListByContract(ContractType={contractType},ContractKey={contractKey})")]
    public async Task<ActionResult<ICollection<ContractDraftDto>>> List([FromRoute] string contractType, [FromRoute] Guid contractKey)
    {
        if (!Enum.TryParse<ContractDraftContractType>(contractType?.Trim('\''), true, out var type))
            return BadRequest(ContractDraftActionParameters.InvalidContractTypeMessage);

        return Ok(await service.ListByContractAsync(type, contractKey));
    }
}
