using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Domain.Constants;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

/// <summary>Parâmetros da empresa: linha única (chave 1), então só há GET e PATCH (PATCH só para administradores).</summary>
public class CompanySettingsController(CompanySettingsService service, IUserPermissions userPermissions) : ODataController
{
    [EnableQuery]
    public virtual async Task<ActionResult> Get()
    {
        return Ok(new[] { await service.GetAsync() }.AsQueryable());
    }

    [EnableQuery]
    public virtual async Task<ActionResult> Get([FromRoute] int key)
    {
        if (key != 1)
        {
            return NotFound();
        }

        return Ok(SingleResult.Create(new[] { await service.GetAsync() }.AsQueryable()));
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromODataUri] int key, [FromBody] Delta<CompanySettings> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Papel ADMIN via perfil (HasRoleAsync), não a flag IsAdmin do usuário.
        if (!await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin))
        {
            return StatusCode(403, "Somente administradores podem alterar os parâmetros da empresa.");
        }

        if (key != 1)
        {
            return NotFound();
        }

        try
        {
            var next = new CompanySettings { Id = 1 };
            var current = await service.GetAsync();
            next.RequireCostCenter = current.RequireCostCenter;
            next.RequireLedgerAccount = current.RequireLedgerAccount;
            patch.Patch(next);
            await service.UpdateAsync(next.RequireCostCenter, next.RequireLedgerAccount);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}
