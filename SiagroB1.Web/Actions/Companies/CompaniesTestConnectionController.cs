using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Domain.Constants;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Actions.Companies;

public class CompaniesTestConnectionController(
    CompaniesTestConnectionService service,
    IUserPermissions userPermissions) : ODataController
{
    [HttpPost("odata/CompaniesTestConnection")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (!User.IsAdmin() && !await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão de administrador para gerenciar empresas.");

        if (parameters is null || !parameters.ContainsKey("ConnectionString"))
            return BadRequest("Parâmetro obrigatório: ConnectionString.");

        var code = parameters.TryGetValue("Code", out var c) ? c?.ToString() : null;

        return Ok(await service.ExecuteAsync(code, parameters["ConnectionString"]?.ToString() ?? string.Empty));
    }
}
