using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Financials;

/// <summary>Adiantamentos que podem ser compensados no provisório (spec 2026-10-14), DTO PascalCase.</summary>
public class FinancialDocumentsGetAdvanceCandidatesController(
    FinancialDocumentsGetAdvanceCandidatesService service) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/FinancialDocumentsGetAdvanceCandidates(Key={key})")]
    public async Task<ActionResult<IEnumerable<FinancialDocumentAdvanceCandidateDto>>> Get([FromRoute] Guid key)
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
