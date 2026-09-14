using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Web.Functions.WarehouseReconciliations;

public class WarehouseReconciliationsAttachmentsDownloadController(
    WarehouseReconciliationAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/WarehouseReconciliationsAttachmentsDownload(Key={key})")]
    public async Task<ActionResult> Get([FromRoute] Guid key)
    {
        var attachment = await service.GetByKey(key);
        return attachment == null
            ? NotFound("Anexo não encontrado.")
            : File(attachment.FileData, attachment.ContentType, attachment.FileName);
    }
}
