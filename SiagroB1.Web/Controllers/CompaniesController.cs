using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Domain.Constants;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Controllers;

public class CompaniesController(
    CompaniesCreateService createService,
    CompaniesUpdateService updateService,
    CompaniesDeleteService deleteService,
    CompaniesGetService getService,
    IUserPermissions userPermissions
    ) 
    : ODataController
{
    /// <summary>Empresas são o catálogo de BANCOS do deploy: só administrador mexe (flag legada ou papel ADMIN).</summary>
    private async Task<bool> IsAdminAsync() =>
        User.IsAdmin() || await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin);

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, "Você não tem permissão de administrador para gerenciar empresas.");

    [EnableQuery]
    [HttpGet("odata/Companies")]
    public async Task<ActionResult<IEnumerable<Company>>> Get()
    {
        if (!await IsAdminAsync()) return Forbidden();

        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    [HttpGet("odata/Companies({code})")]
    [HttpGet("odata/Companies/{code}")]
    public async Task<ActionResult<Company>> Get([FromRoute] string code)
    {
        if (!await IsAdminAsync()) return Forbidden();

        var item = await getService.GetByIdAsync(code);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    public async Task<IActionResult> Post([FromBody] Company entity)
    {
        if (!await IsAdminAsync()) return Forbidden();

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            // O serviço devolve a cópia com a senha mascarada.
            var saved = await createService.ExecuteAsync(entity);

            return Created(saved);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException or ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }

    public async Task<IActionResult> Put([FromRoute] string code, [FromBody] Company entity)
    {
        if (!await IsAdminAsync()) return Forbidden();

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(code, entity);
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException or ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromRoute]string code)
    {
        if (!await IsAdminAsync()) return Forbidden();

        try
        {
            await deleteService.ExecuteAsync(code);
            
            return NoContent();
        }
        catch (Exception ex)
        {
            return ex switch
            {
                NotFoundException => NotFound(ex.Message),
                ApplicationException => BadRequest(ex.Message),
                _ => StatusCode(500, ex.Message)
            };
        }
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] string code, [FromBody] Delta<Company> patch)
    {
        if (!await IsAdminAsync()) return Forbidden();

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        Company? t = await getService.GetByIdAsync(code);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(code, t);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException or ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}