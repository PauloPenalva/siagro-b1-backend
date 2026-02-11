using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsAttachmentsListByContractController(
    PurchaseContractsAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/PurchaseContractsAttachmentsListByContract(ContractKey={key})")]
    public ActionResult<ICollection<PurchaseContractAttachmentsDto>> ListAttachmentsByContract([FromRoute] Guid key)
    {
        return Ok(service.ListAttachmentsByContract(key));
    }
}