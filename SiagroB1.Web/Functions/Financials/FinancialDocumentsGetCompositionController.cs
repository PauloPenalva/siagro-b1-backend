using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Financials;

/// <summary>Composição de um título (spec 2026-10-13): Lines e Candidates, DTO PascalCase.</summary>
public class FinancialDocumentsGetCompositionController(FinancialDocumentsGetCompositionService service) : ODataController
{
    [HttpGet("odata/FinancialDocumentsGetComposition(Key={key})")]
    public async Task<IActionResult> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
