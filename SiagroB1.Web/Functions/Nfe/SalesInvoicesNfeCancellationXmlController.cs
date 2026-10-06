using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

public class SalesInvoicesNfeCancellationXmlController(SalesInvoicesNfeCancellationXmlDownloadService service) : ODataController
{
    [HttpGet("odata/SalesInvoicesNfeCancellationXml(Key={key})")]
    public async Task<ActionResult> Download([FromRoute] Guid key)
    {
        try
        {
            var (bytes, fileName) = await service.ExecuteAsync(key);
            return File(bytes, "application/xml", fileName);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
