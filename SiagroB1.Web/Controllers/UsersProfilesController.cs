using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.UsersProfiles;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class UsersProfilesController(
    UsersProfilesCreateService createService,
    UsersProfilesUpdateService updateService,
    UsersProfilesDeleteService deleteService,
    UsersProfilesGetService getService
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/Users({userId})/Profiles")]
    [HttpGet("odata/Users/{userId}/Profiles")]
    public ActionResult<IEnumerable<UserProfile>> Get(Guid userId)
    {
        return Ok(getService.QueryAll(userId));
    }

    [EnableQuery]
    [HttpGet("odata/Users({userId})/Profiles({userProfileId})")]
    [HttpGet("odata/Users/{userId}/Profiles/{userProfileId}")]
    public async Task<ActionResult<UserProfile>> Get([FromRoute] Guid userId, [FromRoute] Guid userProfileId)
    {
        var item = await getService.GetByIdAsync(userId, userProfileId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpPost("odata/Users({userId})/Profiles")]
    [HttpPost("odata/Users/{userId}/Profiles")]
    public async Task<IActionResult> Post([FromRoute] Guid userId, [FromBody] UserProfile entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            await createService.ExecuteAsync(userId, entity);

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
    
    [HttpPut("odata/Users({userId})/Profiles({userProfileId})")]
    [HttpPut("odata/Users/{userId}/Profiles/{userProfileId}")]
    public async Task<IActionResult> Put(
        [FromRoute] Guid userId, 
        [FromRoute] Guid userProfileId, 
        [FromBody] UserProfile entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(userId, userProfileId, entity);
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
    
    [HttpDelete("odata/Users({userId})/Profiles({userProfileId})")]
    [HttpDelete("odata/Users/{userId}/Profiles/{userProfileId}")]
    public async Task<IActionResult> Delete([FromRoute] Guid userId, [FromRoute] Guid userProfileId)
    {
        try
        {
            await deleteService.ExecuteAsync(userId, userProfileId);
            
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
    
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Users({userId})/Profiles({userProfileId})")]
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Users/{userId}/Profiles/{userProfileId}")]
    public async Task<IActionResult> Patch([FromRoute] Guid userId, [FromRoute] Guid userProfileId, [FromBody] Delta<UserProfile> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await getService.GetByIdAsync(userId, userProfileId);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(userId, userProfileId, t);
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