
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class ProcessingCostDryingParametersController(
    IProcessingCostDryingParameterService descontoSecagemService
    ) : ODataBaseController<ProcessingCostDryingParameter, int>(descontoSecagemService)
{
    protected readonly IProcessingCostDryingParameterService _descontoSecagemService = descontoSecagemService;
    
    [HttpPost("odata/ProcessingCosts({processingCostCode})/DryingParameters")]
    public virtual async Task<IActionResult> CreateDryingParametersAsync([FromODataUri] string processingCostCode, [FromBody] ProcessingCostDryingParameter entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await _descontoSecagemService.CreateAsync(processingCostCode, entity);

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
    [HttpGet("odata/ProcessingCosts/{processingCostCode}/DryingParameters({itemId})")]
    [HttpGet("odata/ProcessingCosts({processingCostCode})/DryingParameters({itemId})")]
    public virtual async Task<IActionResult> GetDryingParametersAsync([FromODataUri] string processingCostCode, [FromODataUri] int itemId)
    {
        var item = await _descontoSecagemService.FindByKeyAsync(processingCostCode, itemId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);

    }

    [HttpGet("odata/ProcessingCosts({processingCostCode})/DryingParameters")]
    [EnableQuery]
    public ActionResult<IQueryable<ProcessingCostDryingParameter>> GetDDryingParameters([FromRoute] string processingCostCode)
    {
        return Ok(_descontoSecagemService.GetAllByTabelaCustoId(processingCostCode));
    }
    
    
}
