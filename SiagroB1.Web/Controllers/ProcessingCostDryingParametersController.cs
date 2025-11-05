using SiagroB1.Domain.Entities;
using SiagroB1.Core.Interfaces;
using SiagroB1.Web.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Domain.Exceptions;
using Microsoft.AspNetCore.OData.Query;

namespace SiagroB1.Web.Controllers
{
    public class ProcessingCostDryingParametersController(
        IProcessingCostDryingParameterService descontoSecagemService
        ) : ODataBaseController<ProcessingCostDryingParameter, int>(descontoSecagemService)
    {
        protected readonly IProcessingCostDryingParameterService _descontoSecagemService = descontoSecagemService;
        
        [HttpPost("odata/ProcessingCosts({tabelaCustoId})/DryingParameters")]
        public virtual async Task<IActionResult> PostAsync([FromODataUri] string tabelaCustoId, [FromBody] ProcessingCostDryingParameter entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _descontoSecagemService.CreateAsync(tabelaCustoId, entity);

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
        [HttpGet("odata/ProcessingCosts/{tabelaCustoId}/DryingParameters({itemId})")]
        [HttpGet("odata/ProcessingCosts({tabelaCustoId})/DryingParameters({itemId})")]
        public virtual async Task<IActionResult> GetAsync([FromODataUri] string tabelaCustoId, [FromODataUri] int itemId)
        {
            var item = await _descontoSecagemService.FindByKeyAsync(tabelaCustoId, itemId);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);

        }

        [HttpGet("odata/ProcessingCosts({tabelaCustoId})/DryingParameters")]
        [EnableQuery]
        public ActionResult<IQueryable<ProcessingCostDryingParameter>> GetDescontosSecagem([FromRoute] string tabelaCustoId)
        {
            return Ok(_descontoSecagemService.GetAllByTabelaCustoId(tabelaCustoId));
        }
        
        
    }
}