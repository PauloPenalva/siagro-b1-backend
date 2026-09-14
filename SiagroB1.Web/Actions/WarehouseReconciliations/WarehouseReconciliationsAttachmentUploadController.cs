using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WarehouseReconciliations;

public class WarehouseReconciliationsAttachmentUploadController(
    WarehouseReconciliationAttachmentsCreateService service) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsAttachmentUpload")]
    public async Task<ActionResult> Upload([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null ||
            !parameters.TryGetValue("ReconciliationKey", out var keyObj) || keyObj is null ||
            !parameters.TryGetValue("File", out var fileObj) || fileObj is null ||
            !parameters.TryGetValue("Description", out var descriptionObj) || descriptionObj is null)
            return BadRequest("ReconciliationKey, File e Description são obrigatórios.");

        parameters.TryGetValue("FileName", out var fileNameObj);
        parameters.TryGetValue("ContentType", out var contentTypeObj);

        try
        {
            await service.SaveAsync((Guid)keyObj, new WarehouseReconciliationAttachment
            {
                Description = descriptionObj.ToString()!,
                FileName = fileNameObj?.ToString() ?? "anexo",
                ContentType = contentTypeObj?.ToString() ?? "application/octet-stream",
                FileData = Convert.FromBase64String(fileObj.ToString()!),
                CreatedAt = DateTime.Now,
                CreatedBy = User.Identity?.Name ?? "unknown",
            });
            return Ok();
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
