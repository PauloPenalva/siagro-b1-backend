using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.SalesContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.SalesContracts;

public class SalesContractsAttachmentsDownloadController(
    SalesContractsAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/SalesContractsAttachmentsDownload(Key={key})")]
    public async Task<ActionResult> GetStream([FromRoute] Guid key)
    {
        var attachment = await service.GetByKey(key);
        return attachment == null 
            ? throw new NotFoundException("Attachment not found") 
            : File(attachment.FileData, attachment.ContentType, attachment.FileName);
    }
}