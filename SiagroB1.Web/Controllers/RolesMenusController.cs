using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.RolesMenus;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class RolesMenusController(
    RolesMenusCreateService createService,
    RolesMenusUpdateService updateService,
    RolesMenusDeleteService deleteService,
    RolesMenusGetService getService
    ) 
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/Roles({roleCode})/Menus")]
    [HttpGet("odata/Roles/{roleCode}/Menus")]
    public ActionResult<IEnumerable<ProfileRole>> Get(string roleCode)
    {
        return Ok(getService.QueryAll(roleCode));
    }

    [EnableQuery]
    [HttpGet("odata/Roles({roleCode})/Menus({roleMenuId})")]
    [HttpGet("odata/Roles/{roleCode}/Menus/{roleMenuId}")]
    public async Task<ActionResult<Profile>> Get([FromRoute] string roleCode, [FromRoute] Guid roleMenuId)
    {
        var item = await getService.GetByIdAsync(roleCode, roleMenuId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpPost("odata/Roles({roleCode})/Menus")]
    [HttpPost("odata/Roles/{roleCode}/Menus")]
    public async Task<IActionResult> Post([FromRoute] string roleCode, [FromBody] RoleMenu entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            await createService.ExecuteAsync(roleCode, entity);

            return Created(entity);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }
    
    [HttpPut("odata/Roles({roleCode})/Menus({roleMenuId})")]
    [HttpPut("odata/Roles/{roleCode}/Menus/{roleMenuId}")]
    public async Task<IActionResult> Put(
        [FromRoute] string roleCode, 
        [FromRoute] Guid roleMenuId, 
        [FromBody] RoleMenu entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(roleCode, roleMenuId, entity);
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception ex)
        {
            if (ex is ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
    
    [HttpDelete("odata/Roles({roleCode})/Menus({roleMenuId})")]
    [HttpDelete("odata/Roles/{roleCode}/Menus/{roleMenuId}")]
    public async Task<IActionResult> Delete([FromRoute] string roleCode, [FromRoute] Guid roleMenuId)
    {
        try
        {
            await deleteService.ExecuteAsync(roleCode, roleMenuId);
            
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
    
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Roles({roleCode})/Menus({roleMenuId})")]
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Roles/{roleCode}/Menus/{roleMenuId}")]
    public async Task<IActionResult> Patch([FromRoute] string roleCode, [FromRoute] Guid roleMenuId, [FromBody] Delta<RoleMenu> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await getService.GetByIdAsync(roleCode, roleMenuId);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(roleCode, roleMenuId, t);
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