
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Base
{
    public abstract class ODataBaseController<T, ID>(IBaseService<T, ID> service) : ODataController where T : class
    {

        private readonly IBaseService<T, ID> _service = service;
        
        [EnableQuery]
        public virtual ActionResult<IEnumerable<T>> Get()
        {
            return Ok(_service.QueryAll());
        }

        [EnableQuery]
        public virtual async Task<ActionResult<T>> Get([FromRoute] ID key)
        {
            var item = await _service.GetByIdAsync(key);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);
        }
        
        public virtual async Task<IActionResult> Post([FromBody] T entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            
            try
            {
                await _service.CreateAsync(entity);

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

        public virtual async Task<IActionResult> Put([FromRoute] ID key, [FromBody] T entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _service.UpdateAsync(key, entity);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
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

        public virtual async Task<IActionResult> Delete([FromRoute] ID key)
        {
            try
            {
                bool success = await _service.DeleteAsync(key);

                if (!success)
                {
                    return NotFound();
                }

                return NoContent();

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

        [AcceptVerbs("PATCH", "MERGE")]
        public virtual async Task<IActionResult> Patch([FromODataUri] ID key, [FromBody] Delta<T> patch)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            T? t = await _service.GetByIdAsync(key);

            if (t == null)
            {
                return NotFound();
            }

            try
            {
                patch.Patch(t);

                await _service.UpdateAsync(key, t);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
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
}