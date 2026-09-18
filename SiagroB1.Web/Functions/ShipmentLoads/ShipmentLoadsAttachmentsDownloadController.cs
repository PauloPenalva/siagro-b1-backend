using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>
/// Devolve o arquivo de um anexo da carga (GAC-1171), no molde de
/// <c>WarehouseReconciliationsAttachmentsDownload</c>: o binário sai como arquivo, fora da
/// leitura normal do OData.
/// </summary>
public class ShipmentLoadsAttachmentsDownloadController(
    ShipmentLoadAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/ShipmentLoadsAttachmentsDownload(Key={key})")]
    public async Task<ActionResult> Download([FromRoute] Guid key)
    {
        var attachment = await service.GetByKey(key);

        return attachment is null
            ? NotFound("Anexo não encontrado.")
            : File(attachment.FileData, attachment.ContentType, attachment.FileName);
    }
}
