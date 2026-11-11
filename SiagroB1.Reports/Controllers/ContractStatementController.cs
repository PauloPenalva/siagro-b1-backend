using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Extrato do Contrato: conferência físico/fiscal/financeiro de um contrato de compra ou venda.</summary>
[ApiController]
[Route("/reports/ContractStatement")]
public class ContractStatementController(
    ContractStatementReportService service,
    PurchaseContractLotStatementReportService lotService) : ControllerBase
{
    [HttpPost("purchase/{key:guid}/print")]
    public Task<IActionResult> Purchase(Guid key) => PrintAsync(ContractStatementSide.Purchase, key);

    [HttpPost("sales/{key:guid}/print")]
    public Task<IActionResult> Sales(Guid key) => PrintAsync(ContractStatementSide.Sales, key);

    /// <summary>Extrato com o detalhado dos lotes de origem (GAC-1198).</summary>
    [HttpPost("purchase/{key:guid}/lots/print")]
    public Task<IActionResult> PurchaseWithLots(Guid key, CancellationToken ct) =>
        RespondAsync(() => lotService.ExecuteAsync(key, ct), $"ExtratoContratoLotes-{key}.pdf");

    private Task<IActionResult> PrintAsync(ContractStatementSide side, Guid key) =>
        RespondAsync(() => service.ExecuteAsync(side, key), $"ExtratoContrato-{key}.pdf");

    private async Task<IActionResult> RespondAsync(Func<Task<byte[]>> generate, string fileName)
    {
        try
        {
            var pdf = await generate();
            Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
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
