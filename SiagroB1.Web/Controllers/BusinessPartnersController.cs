using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Web.Controllers
{
    public class BusinessPartnersController(IBusinessPartnerService service) 
        : ODataController
    {
        [EnableQuery]
        public virtual ActionResult<IEnumerable<BusinessPartnerModel>> Get()
        {
            return Ok(service.QueryAll());
        }

        [EnableQuery]
        public virtual async Task<ActionResult<BusinessPartnerModel>> Get([FromRoute] string key)
        {
            var item = await service.GetByIdAsync(key);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);
        }
        
        public virtual async Task<IActionResult> Post([FromBody] BusinessPartnerModel entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            
            try
            {
                await service.CreateAsync(entity);

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

        public virtual async Task<IActionResult> Put([FromRoute] string key, [FromBody] BusinessPartnerModel entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await service.UpdateAsync(key, entity);
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

        public virtual async Task<IActionResult> Delete([FromRoute] string key)
        {
            try
            {
                bool success = await service.DeleteAsync(key);

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
        public virtual async Task<IActionResult> Patch([FromODataUri] string key, [FromBody] Delta<BusinessPartnerModel> patch)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            BusinessPartnerModel? t = await service.GetByIdAsync(key);

            if (t == null)
            {
                return NotFound();
            }

            try
            {
                patch.Patch(t);

                await service.UpdateAsync(key, t);
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