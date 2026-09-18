using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Anexa um documento à carga (GAC-1171), no molde da Conferência de Armazém mais o tipo.
/// </summary>
public class ShipmentLoadsAttachmentUploadController(
    ShipmentLoadAttachmentsCreateService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsAttachmentUpload")]
    public async Task<ActionResult> Upload([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado, e o TryGetValue de um
        // parâmetro anulável devolve true com valor nulo — daí as duas checagens.
        if (parameters is null ||
            !parameters.TryGetValue("LoadKey", out var keyObj) || keyObj is null ||
            !parameters.TryGetValue("File", out var fileObj) || fileObj is null ||
            !parameters.TryGetValue("Description", out var descriptionObj) || descriptionObj is null)
            return BadRequest("Carga, arquivo e descrição são obrigatórios.");

        parameters.TryGetValue("AttachmentType", out var typeObj);
        parameters.TryGetValue("FileName", out var fileNameObj);
        parameters.TryGetValue("ContentType", out var contentTypeObj);

        try
        {
            await service.SaveAsync((Guid) keyObj, new ShipmentLoadAttachment
            {
                // O tipo viaja como string. Valor desconhecido cai em "Outro" em vez de derrubar
                // o upload: o arquivo é o que importa, a classificação é editável depois.
                AttachmentType = Enum.TryParse<ShipmentLoadAttachmentType>(
                    typeObj as string, out var parsedType)
                    ? parsedType
                    : ShipmentLoadAttachmentType.Other,
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
