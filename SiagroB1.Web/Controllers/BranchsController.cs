using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Domain.Entities;
using SiagroB1.Core.Services;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace SiagroB1.Web.Controllers
{
    [Route("odata/Filiais")]
    public class BranchsController(IBranchService service) : ODataController
    {
        private readonly IBranchService _service = service;

        [EnableQuery]
        public ActionResult<IEnumerable<Branch>> Get()
        {
            return Ok(_service.QueryAll());
        }

        [EnableQuery]
        public async Task<ActionResult<Branch>> Get([FromRoute] string key)
        {
            var item = await _service.GetByIdAsync(key);

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

            await _service.CreateAsync(branch);

            return Created(branch);
        }

        public async Task<IActionResult> Put([FromRoute] string key, [FromBody] Branch branch)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (key != branch.Key)
            {
                return BadRequest();
            }

            try
            {
                await _service.UpdateAsync(key, branch);
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

            bool success = await _service.DeleteAsync(key);

            if (!success)
            {
                return NotFound();
            }

            return NoContent();
        }
    }
}