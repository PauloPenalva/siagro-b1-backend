using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Domain.Entities;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers
{
    public class ProcessingCostDryingDetailsController(IProcessingCostDryingDetailService service) 
        : ODataBaseController<ProcessingCostDryingDetail, Guid>(service)
    {
        private readonly IProcessingCostDryingDetailService _service = service;
        
        [HttpPost("odata/ProcessingCosts({processingCostKey})/DryingDetails")]
        public virtual async Task<IActionResult> PostAsync([FromODataUri] Guid processingCostKey, [FromBody] ProcessingCostDryingDetail entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _service.CreateAsync(processingCostKey, entity);

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

        // quando se esta alterando a entidade pai, apos incluir um item de linha, a OpenUI5 esta chamando neste formato a requisição
        // fora de padrão
        [HttpGet("odata/ProcessingCosts/{processingCostKey}/DryingDetails({key})")]
        [HttpGet("odata/ProcessingCosts({processingCostKey})/DryingDetails({key})")]
        public virtual async Task<IActionResult> GetAsync([FromODataUri] Guid processingCostKey, [FromODataUri] Guid key)
        {
            var item = await _service.FindByKeyAsync(processingCostKey, key);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);

        }

        [HttpGet("odata/ProcessingCosts({processingCostKey})/DryingDetails")]
        [EnableQuery]
        public ActionResult<IQueryable<ProcessingCostDryingParameter>> GetDryingDetails([FromRoute] Guid processingCostKey)
        {
            return Ok(_service.GetAllByProcessingCostKey(processingCostKey));
        }

    }
}