using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class BranchNfeSettingsSaveController(BranchNfeSettingsService service) : ODataController
{
    [HttpPost("odata/BranchNfeSettingsSave")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ODataActionParameters chega NULO quando o corpo não casa com o EDM.
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, Environment, Series, NextNumber.");

        try
        {
            return Ok(await service.SaveAsync(
                parameters["BranchCode"]?.ToString() ?? string.Empty,
                (NfeEnvironment)Convert.ToInt32(parameters["Environment"]),
                Convert.ToInt32(parameters["Series"]),
                Convert.ToInt32(parameters["NextNumber"]),
                User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
