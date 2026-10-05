using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Web.Actions.Nfe;

public class BranchNfeSettingsTestConnectionController(BranchNfeSettingsService service) : ODataController
{
    [HttpPost("odata/BranchNfeSettingsTestConnection")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetro obrigatório: BranchCode.");

        try
        {
            return Ok(await service.TestConnectionAsync(parameters["BranchCode"]?.ToString() ?? string.Empty));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (NfeCommunicationException e)
        {
            return BadRequest($"Sem resposta da SEFAZ: {e.Message}");
        }
    }
}
