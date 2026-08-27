using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsSetSignatureStatusController(
    SalesContractsSetSignatureStatusService service) : ODataController
{
    [HttpPost("odata/SalesContractsSetSignatureStatus")]
    public async Task<IActionResult> SetSignatureStatusAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // O OData entrega NULO aqui quando o corpo não traz nenhum parâmetro declarado no EDM.
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Missing required parameters");

            // Parâmetro de string é anulável mesmo quando presente, e aqui nulo é valor legítimo:
            // significa limpar a situação de assinatura.
            parameters.TryGetValue("SignatureStatus", out var statusObj);
            var statusText = statusObj?.ToString();

            SignatureStatus? status = null;
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                if (!Enum.TryParse<SignatureStatus>(statusText, out var parsed))
                    return BadRequest("Situação de assinatura inválida.");

                status = parsed;
            }

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());
            await service.ExecuteAsync(key, status, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
