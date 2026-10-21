using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Extrato do Contrato: conferência físico/fiscal/financeiro de um contrato de compra ou venda.</summary>
[ApiController]
[Route("/reports/ContractStatement")]
public class ContractStatementController(ContractStatementReportService service) : ControllerBase
{
    [HttpPost("purchase/{key:guid}/print")]
    public Task<IActionResult> Purchase(Guid key) => PrintAsync(ContractStatementSide.Purchase, key);

    [HttpPost("sales/{key:guid}/print")]
    public Task<IActionResult> Sales(Guid key) => PrintAsync(ContractStatementSide.Sales, key);

    private async Task<IActionResult> PrintAsync(ContractStatementSide side, Guid key)
    {
        try
        {
            var pdf = await service.ExecuteAsync(side, key);
            Response.Headers.ContentDisposition = $"inline; filename=\"ExtratoContrato-{key}.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }
}
