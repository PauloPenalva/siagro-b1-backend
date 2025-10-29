using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Domain.Entities;
using SiagroB1.Core.Services;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace SiagroB1.Web.Controllers
{
    [Route("odata/Filiais")]
    public class FiliaisController(IFilialService service) : ODataController
    {
        private readonly IFilialService _service = service;

        [EnableQuery]
        public ActionResult<IEnumerable<Filial>> Get()
        {
            return Ok(_service.QueryAll());
        }

        [EnableQuery]
        public async Task<ActionResult<Filial>> Get([FromRoute] int key)
        {
            var item = await _service.GetByIdAsync(key);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);
        }

        public async Task<IActionResult> Post([FromBody] Filial filial)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _service.CreateAsync(filial);

            return Created(filial);
        }

        public async Task<IActionResult> Put([FromRoute] int key, [FromBody] Filial filial)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (key != filial.Id)
            {
                return BadRequest();
            }

            try
            {
                await _service.UpdateAsync(key, filial);
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
        
        public async Task<IActionResult> Delete([FromRoute] int key)
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