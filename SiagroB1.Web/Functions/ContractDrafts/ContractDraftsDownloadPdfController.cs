using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsDownloadPdfController(ContractDraftsGetPdfService service) : ODataController
{
    [HttpGet("odata/ContractDraftsDownloadPdf(Key={key})")]
    public async Task<ActionResult> Download([FromRoute] Guid key, CancellationToken ct)
    {
        try
        {
            var (bytes, fileName) = await service.ExecuteAsync(key, ct);
            return File(bytes, "application/pdf", fileName);
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }
}
