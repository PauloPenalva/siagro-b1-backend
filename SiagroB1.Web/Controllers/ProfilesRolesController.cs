using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ProfilesRoles;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class ProfilesRolesController(
    ProfilesRolesCreateService createService,
    ProfilesRolesUpdateService updateService,
    ProfilesRolesDeleteService deleteService,
    ProfilesRolesGetService getService
    ) 
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/Profiles({profileCode})/Roles")]
    [HttpGet("odata/Profiles/{profileCode}/Roles")]
    public ActionResult<IEnumerable<ProfileRole>> Get(string profileCode)
    {
        return Ok(getService.QueryAll(profileCode));
    }

    [EnableQuery]
    [HttpGet("odata/Profiles({profileCode})/Roles({profileRoleId})")]
    [HttpGet("odata/Profiles/{profileCode}/Roles/{profileRoleId}")]
    public async Task<ActionResult<Profile>> Get([FromRoute] string profileCode, [FromRoute] Guid profileRoleId)
    {
        var item = await getService.GetByIdAsync(profileCode, profileRoleId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpPost("odata/Profiles({profileCode})/Roles")]
    [HttpPost("odata/Profiles/{profileCode}/Roles")]
    public async Task<IActionResult> Post([FromRoute] string profileCode, [FromBody] ProfileRole entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            await createService.ExecuteAsync(profileCode, entity);

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
    
    [HttpPut("odata/Profiles({profileCode})/Roles({profileRoleId})")]
    [HttpPut("odata/Profiles/{profileCode}/Roles/{profileRoleId}")]
    public async Task<IActionResult> Put(
        [FromRoute] string profileCode, 
        [FromRoute] Guid profileRoleId, 
        [FromBody] ProfileRole entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(profileCode, profileRoleId, entity);
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
    
    [HttpDelete("odata/Profiles({profileCode})/Roles({profileRoleId})")]
    [HttpDelete("odata/Profiles/{profileCode}/Roles/{profileRoleId}")]
    public async Task<IActionResult> Delete([FromRoute] string profileCode, [FromRoute] Guid profileRoleId)
    {
        try
        {
            await deleteService.ExecuteAsync(profileCode, profileRoleId);
            
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
    
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Profiles({profileCode})/Roles({profileRoleId})")]
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Profiles/{profileCode}/Roles/{profileRoleId}")]
    public async Task<IActionResult> Patch([FromRoute] string profileCode, [FromRoute] Guid profileRoleId, [FromBody] Delta<ProfileRole> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await getService.GetByIdAsync(profileCode, profileRoleId);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(profileCode, profileRoleId, t);
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