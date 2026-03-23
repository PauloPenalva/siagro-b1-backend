using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageAddressesReport")]
public class StorageAddressesReportController(StorageAddressReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> GeneratePdf([FromBody] StorageAddressReportRequest request, CancellationToken ct)
    {
        var userName = User?.Identity?.Name ?? "";
        var bytes = await service.GeneratePdfAsync(request, userName, ct);
        return File(bytes, "application/pdf", "storage-address-report.pdf");
    }
}