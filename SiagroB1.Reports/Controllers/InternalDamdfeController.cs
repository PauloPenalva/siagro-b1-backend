using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>
/// DAMDFE para uso interno do SiagroB1.Web (ZIP da entrega B). Mesmo contrato do <see cref="InternalDanfeController"/>: rota fora
/// de <c>/reports</c> (o Gateway nunca a expõe), empresa em <c>X-Company</c> e acesso por <c>X-Internal-Key</c> igual a
/// <c>Reports:InternalApiKey</c>. Qualquer falha de autenticação responde 404.
/// </summary>
[ApiController]
[Route("internal/damdfe")]
public class InternalDamdfeController(ICompanyCatalog catalog, ICompanyContext companyContext, IConfiguration configuration)
    : ControllerBase
{
    [HttpGet("{key:guid}")]
    public async Task<IActionResult> Get(Guid key)
    {
        var expected = configuration["Reports:InternalApiKey"];
        var sent = Request.Headers["X-Internal-Key"].ToString();

        if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(expected, sent))
            return NotFound();

        var company = await catalog.GetByCodeAsync(Request.Headers["X-Company"].ToString(), HttpContext.RequestAborted);

        if (company is null)
            return NotFound("Empresa não encontrada.");

        // Antes de resolver o DamdfeReportService: o AppDbContext lê a conexão da empresa ao nascer.
        companyContext.Set(company);
        var service = HttpContext.RequestServices.GetRequiredService<DamdfeReportService>();

        try
        {
            return File(await service.RenderAsync(key), "application/pdf");
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static bool FixedTimeEquals(string expected, string sent) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected)),
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sent)));
}
