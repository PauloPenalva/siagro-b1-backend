using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsAttachmentUploadController(PurchaseContractsAttachmentsCreateService service) 
    : ODataController
{
    [HttpPost("odata/PurchaseContractsAttachmentUpload")]
    public async Task<ActionResult> Upload([FromBody] ODataActionParameters parameters)
    {
        if (!parameters.ContainsKey("File") || !parameters.ContainsKey("Description"))
            return BadRequest();

        try
        {
            var contractKey = (Guid) parameters["ContractKey"];
            var description = parameters["Description"].ToString()!;
            var fileBytes = Convert.FromBase64String(parameters["File"].ToString()!);
            var fileName = parameters["FileName"]?.ToString()!;
            var contentType = parameters["ContentType"]?.ToString()!;

            var attachment = new PurchaseContractAttachment
            {
                Description = description,
                FileName = fileName,
                ContentType = contentType,
                FileData =  fileBytes,
                CreatedAt =  DateTime.Now,
                CreatedBy = User.Identity?.Name ?? "unknown"
            };  
          
            await service.SaveAsync(contractKey, attachment);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }
            
            return BadRequest(e.Message);
        }
        
    }
}