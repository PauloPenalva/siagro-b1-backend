using SiagroB1.Domain.Entities;
using SiagroB1.Core.Interfaces;
using SiagroB1.Web.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Domain.Exceptions;
using Microsoft.AspNetCore.OData.Query;

namespace SiagroB1.Web.Controllers
{
    public class ProcessingCostQualityParametersController(
        IProcessingCostQualityParameterService qualidadeService
        ) : ODataBaseController<ProcessingCostQualityParameter, string>(qualidadeService)
    {
        protected readonly IProcessingCostQualityParameterService _qualidadeService = qualidadeService;
        
        [HttpPost("odata/ProcessingCosts({tabelaCustoId})/QualityParameters")]
        public virtual async Task<IActionResult> PostAsync([FromODataUri] string tabelaCustoId, [FromBody] ProcessingCostQualityParameter entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _qualidadeService.CreateAsync(tabelaCustoId, entity);

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
        [HttpGet("odata/ProcessingCosts/{tabelaCustoId}/QualityParameters({key})")]
        [HttpGet("odata/ProcessingCosts({tabelaCustoId})/QualityParameters({key})")]
        public virtual async Task<IActionResult> GetAsync([FromODataUri] string tabelaCustoId, [FromODataUri] string key)
        {
            var item = await _qualidadeService.FindByKeyAsync(tabelaCustoId, key);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);

        }

        [HttpGet("odata/ProcessingCosts({tabelaCustoId})/QualityParameters")]
        [EnableQuery]
        public ActionResult<IQueryable<ProcessingCostDryingDetail>> GetQualidades([FromRoute] string tabelaCustoId)
        {
            return Ok(_qualidadeService.GetAllByProcessingCostKey(tabelaCustoId));
        }
        
        
    }
}