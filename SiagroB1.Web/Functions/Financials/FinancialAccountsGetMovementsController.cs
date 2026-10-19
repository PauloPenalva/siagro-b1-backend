using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Financials;

public class FinancialAccountsGetMovementsController(FinancialAccountsGetMovementsService service) : ODataController
{
    // Todos os parâmetros obrigatórios no EDM: parâmetro opcional omitido muda a rota e dá 404.
    [HttpGet("odata/FinancialAccountsGetMovements(AccountCode={accountCode},DateFrom={dateFrom},DateTo={dateTo},OnlyReconciled={onlyReconciled})")]
    public async Task<ActionResult<FinancialAccountMovementsDto>> Get([FromRoute] string accountCode,
        [FromRoute] string dateFrom, [FromRoute] string dateTo, [FromRoute] bool onlyReconciled)
    {
        try
        {
            return Ok(await service.ExecuteAsync(accountCode,
                DateTime.Parse(dateFrom, CultureInfo.InvariantCulture),
                DateTime.Parse(dateTo, CultureInfo.InvariantCulture), onlyReconciled));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
