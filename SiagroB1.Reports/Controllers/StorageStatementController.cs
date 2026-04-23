using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.ReportGenerators;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports")]
public class StorageStatementController(StorageStatementReportGenerator generator) : ControllerBase
{
    [HttpPost("storage-statement")]
    public async Task<IActionResult> GenerateStorageStatement(
        [FromBody] StorageStatementReportFilter filter,
        CancellationToken cancellationToken)
    {
        var pdf = await generator.GeneratePdfAsync(filter, cancellationToken);

        return File(
            pdf,
            "application/pdf",
            $"extrato-entrada-saida-{DateTime.Now:yyyyMMddHHmmss}.pdf");
    }
}