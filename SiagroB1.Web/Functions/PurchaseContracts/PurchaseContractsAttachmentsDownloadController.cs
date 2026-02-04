using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsAttachmentsDownloadController(
    PurchaseContractsAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/PurchaseContractsAttachmentsDownload(Key={key})")]
    public async Task<ActionResult> GetStream([FromRoute] Guid key)
    {
        var attachment = await service.GetByKey(key);
        return attachment == null 
            ? throw new NotFoundException("Attachment not found") 
            : File(attachment.FileData, attachment.ContentType, attachment.FileName);
    }
}