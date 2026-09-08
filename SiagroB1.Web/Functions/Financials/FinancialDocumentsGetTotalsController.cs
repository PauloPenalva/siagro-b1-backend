using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Financials;

public class FinancialDocumentsGetTotalsController(FinancialDocumentsGetTotalsService service)
    : ODataController
{
    // Duas formas declaradas: BranchCode é opcional no EDM, e a chamada natural OMITE o segmento.
    // Sem a rota curta, `FinancialDocumentsGetTotals(Direction='Payable')` toma 404 — e nenhum
    // teste deste projeto exercita roteamento, então a falha só apareceria no navegador.
    [EnableQuery]
    [HttpGet("odata/FinancialDocumentsGetTotals(Direction={direction})")]
    [HttpGet("odata/FinancialDocumentsGetTotals(Direction={direction},BranchCode={branchCode})")]
    public async Task<ActionResult<FinancialDocumentTotalsDto>> Get(
        [FromRoute] string direction, [FromRoute] string? branchCode = null)
    {
        try
        {
            return Ok(await service.ExecuteAsync(direction, branchCode));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
