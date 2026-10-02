using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>
/// Certificado A1 em base64 por action OData — o mesmo caminho do anexo do contrato: multipart
/// não atravessa o Gateway (só /odata, /security e /reports chegam ao backend).
/// </summary>
public class BranchNfeSettingsUploadCertificateController(BranchNfeSettingsService service) : ODataController
{
    [HttpPost("odata/BranchNfeSettingsUploadCertificate")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.ContainsKey("BranchCode") || !parameters.ContainsKey("Pfx"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, Pfx, Password.");

        byte[] pfx;
        try
        {
            pfx = Convert.FromBase64String(parameters["Pfx"]?.ToString() ?? string.Empty);
        }
        catch (FormatException)
        {
            return BadRequest("O arquivo do certificado chegou corrompido.");
        }

        try
        {
            return Ok(await service.UploadCertificateAsync(
                parameters["BranchCode"]?.ToString() ?? string.Empty,
                pfx,
                parameters.TryGetValue("Password", out var password) ? password?.ToString() ?? string.Empty : string.Empty,
                User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
