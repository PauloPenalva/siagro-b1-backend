using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.RolesPermissions;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class RolesPermissionsController(
    RolesPermissionsCreateService createService,
    RolesPermissionsUpdateService updateService,
    RolesPermissionsDeleteService deleteService,
    RolesPermissionsGetService getService
    ) 
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/Roles({roleCode})/Permissions")]
    [HttpGet("odata/Roles/{roleCode}/Permissions")]
    public ActionResult<IEnumerable<ProfileRole>> Get(string roleCode)
    {
        return Ok(getService.QueryAll(roleCode));
    }

    [EnableQuery]
    [HttpGet("odata/Roles({roleCode})/Permissions({rolePermissionId})")]
    [HttpGet("odata/Roles/{roleCode}/Permissions/{rolePermissionId}")]
    public async Task<ActionResult<Profile>> Get([FromRoute] string roleCode, [FromRoute] Guid rolePermissionId)
    {
        var item = await getService.GetByIdAsync(roleCode, rolePermissionId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpPost("odata/Roles({roleCode})/Permissions")]
    [HttpPost("odata/Roles/{roleCode}/Permissions")]
    public async Task<IActionResult> Post([FromRoute] string roleCode, [FromBody] RolePermission entity)
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
    
    [HttpPut("odata/Roles({roleCode})/Permissions({rolePermissionId})")]
    [HttpPut("odata/Roles/{roleCode}/Permissions/{rolePermissionId}")]
    public async Task<IActionResult> Put(
        [FromRoute] string roleCode, 
        [FromRoute] Guid rolePermissionId, 
        [FromBody] RolePermission entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(roleCode, rolePermissionId, entity);
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
    
    [HttpDelete("odata/Roles({roleCode})/Permissions({rolePermissionId})")]
    [HttpDelete("odata/Roles/{roleCode}/Permissions/{rolePermissionId}")]
    public async Task<IActionResult> Delete([FromRoute] string roleCode, [FromRoute] Guid rolePermissionId)
    {
        try
        {
            await deleteService.ExecuteAsync(roleCode, rolePermissionId);
            
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
    
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Roles({roleCode})/Permissions({rolePermissionId})")]
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Roles/{roleCode}/Permissions/{rolePermissionId}")]
    public async Task<IActionResult> Patch([FromRoute] string roleCode, [FromRoute] Guid rolePermissionId, [FromBody] Delta<RolePermission> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await getService.GetByIdAsync(roleCode, rolePermissionId);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(roleCode, rolePermissionId, t);
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