using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.SalesContracts;

namespace SiagroB1.Web.Functions.SalesContracts;

public class SalesContractsAttachmentsListByContractController(
    SalesContractsAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/SalesContractsAttachmentsListByContract(ContractKey={key})")]
    public ActionResult<ICollection<SalesContractAttachmentsDto>> ListAttachmentsByContract([FromRoute] Guid key)
    {
        return Ok(service.ListAttachmentsByContract(key));
    }
}