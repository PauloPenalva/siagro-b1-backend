using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.SalesInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesInvoices;

public class SalesInvoicesSetDocumentNumberController(
        SalesInvoicesSetDocumentNumberService service
    )
    :ODataController
{
    [HttpPost("odata/SalesInvoicesSetDocumentNumber")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) ||
                !parameters.TryGetValue("DocumentNumber", out var documentNumberObj) ||
                !parameters.TryGetValue("DocumentSeries", out var documentSeriesObj) ||
                !parameters.TryGetValue("ChaveNFe",  out var chaveNFeObj)
                )
            {
                return BadRequest("Missing required parameters");
            }
            
            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());
            var documentNumber = documentNumberObj.ToString();
            var documentSeries = documentSeriesObj.ToString();
            var chaveNFe = chaveNFeObj.ToString();
            
            await service.ExecuteAsync(key, documentNumber, documentSeries, chaveNFe, userName);
            
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