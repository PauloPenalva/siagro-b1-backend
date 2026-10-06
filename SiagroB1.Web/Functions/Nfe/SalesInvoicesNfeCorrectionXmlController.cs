using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

public class SalesInvoicesNfeCorrectionXmlController(SalesInvoicesNfeCorrectionXmlDownloadService service) : ODataController
{
    [HttpGet("odata/SalesInvoicesNfeCorrectionXml(Key={key},Sequence={sequence})")]
    public async Task<ActionResult> Download([FromRoute] Guid key, [FromRoute] int sequence)
    {
        try
        {
            var (bytes, fileName) = await service.ExecuteAsync(key, sequence);
            return File(bytes, "application/xml", fileName);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
