using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/PurchaseFiscalReconciliation")]
public class PurchaseFiscalReconciliationController(PurchaseFiscalReconciliationReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] PurchaseFiscalReconciliationReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CardCode) || string.IsNullOrWhiteSpace(request.ItemCode))
            return BadRequest("Informe o fornecedor e o produto.");

        if (request.FromDate > request.ToDate)
            return BadRequest("A data inicial é maior que a final.");

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"purchase-fiscal-reconciliation.pdf\"";
        return File(pdf, "application/pdf");
    }
}
