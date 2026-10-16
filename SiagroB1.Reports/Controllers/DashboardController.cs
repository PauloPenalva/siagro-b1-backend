using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Painel da home. Só STANDALONE: em SAPB1 responde 404 e o frontend deixa a home vazia.</summary>
[ApiController]
[Route("/reports/Dashboard")]
public class DashboardController(DashboardService service, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var erp = configuration.GetValue<string>("Erp") ?? "STANDALONE";
        if (!string.Equals(erp, "STANDALONE", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        return Ok(await service.BuildAsync(DateTime.Today));
    }
}
