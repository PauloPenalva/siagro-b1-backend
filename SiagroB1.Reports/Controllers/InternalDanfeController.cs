using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>
/// DANFE para o job de e-mail da NF-e (SiagroB1.Web). ⚠️ Rota fora de <c>/reports</c> de propósito: o Gateway só
/// encaminha <c>/reports/**</c>, então esta rota nunca fica pública. Sem cookie, a empresa vem no cabeçalho
/// <c>X-Company</c> e o acesso exige <c>X-Internal-Key</c> igual a <c>Reports:InternalApiKey</c> (vazia = desligada).
/// Qualquer falha de autenticação responde 404, sem revelar que a rota existe.
/// </summary>
[ApiController]
[Route("internal/danfe")]
public class InternalDanfeController(ICompanyCatalog catalog, ICompanyContext companyContext, IConfiguration configuration)
    : ControllerBase
{
    [HttpGet("{type}/{key:guid}")]
    public async Task<IActionResult> Get(string type, Guid key)
    {
        var expected = configuration["Reports:InternalApiKey"];
        var sent = Request.Headers["X-Internal-Key"].ToString();

        if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(expected, sent))
            return NotFound();

        if (type is not ("sales" or "purchase"))
            return NotFound();

        var company = await catalog.GetByCodeAsync(Request.Headers["X-Company"].ToString(), HttpContext.RequestAborted);

        if (company is null)
            return NotFound("Empresa não encontrada.");

        // Antes de resolver o DanfeReportService: o AppDbContext lê a conexão da empresa ao nascer.
        companyContext.Set(company);
        var service = HttpContext.RequestServices.GetRequiredService<DanfeReportService>();

        try
        {
            var (pdf, _) = type == "purchase"
                ? await service.GeneratePurchasePdfAsync(key)
                : await service.GeneratePdfAsync(key);

            return File(pdf, "application/pdf");
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
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
