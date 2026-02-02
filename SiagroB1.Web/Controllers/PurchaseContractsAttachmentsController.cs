using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsAttachmentsController(
    PurchaseContractsAttachmentsGetService service) : ODataController
{
    [HttpGet]
    
    
    
    
    [HttpGet("odata/PurchaseContractsAttachments({key:guid})/$value")]
    [HttpGet("odata/PurchaseContractsAttachments/{key:guid}/$value")]
    public async Task<ActionResult> GetStream([FromRoute] Guid key)
    {
        var attachment = await service.GetByKey(key);
        return attachment == null 
            ? throw new NotFoundException("Attachment not found") 
            : File(attachment.FileData, attachment.ContentType, attachment.FileName);
    }
}