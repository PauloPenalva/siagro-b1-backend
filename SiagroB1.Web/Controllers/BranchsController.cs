using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Controllers;

[Route("odata/Branchs")]
public class BranchsController(IBranchService service) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<Branch>> Get()
    {
        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<Branch>> Get([FromRoute] string key)
    {
        var item = await service.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] Branch branch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        await service.CreateAsync(branch);

        return Created(branch);
    }

    public async Task<IActionResult> Put([FromRoute] string key, [FromBody] Branch branch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (key != branch.Code)
        {
            return BadRequest();
        }

        try
        {
            await service.UpdateAsync(key, branch);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
    
    public async Task<IActionResult> Delete([FromRoute] string key)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var success = await service.DeleteAsync(key);

        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
}
