using SiagroB1.Web.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Domain.Shared.Base.Exceptions;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Controllers;

public class ProcessingCostQualityParametersController(
    IProcessingCostQualityParameterService qualidadeService
    ) : ODataBaseController<ProcessingCostQualityParameter, string>(qualidadeService)
{
    protected readonly IProcessingCostQualityParameterService _qualidadeService = qualidadeService;
    
    [HttpPost("odata/ProcessingCosts({processingCostCode})/QualityParameters")]
    public virtual async Task<IActionResult> PostAsync([FromODataUri] string processingCostCode, [FromBody] ProcessingCostQualityParameter entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await _qualidadeService.CreateAsync(processingCostCode, entity);

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
    [HttpGet("odata/ProcessingCosts/{processingCostCode}/QualityParameters({itemId})")]
    [HttpGet("odata/ProcessingCosts({processingCostCode})/QualityParameters({itemId})")]
    public virtual async Task<IActionResult> GetAsync([FromODataUri] string processingCostCode, [FromODataUri] int itemId)
    {
        var item = await _qualidadeService.FindByKeyAsync(processingCostCode, itemId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);

    }

    [HttpGet("odata/ProcessingCosts({processingCostCode})/QualityParameters")]
    [EnableQuery]
    public ActionResult<IQueryable<ProcessingCostDryingDetail>> GetQualidades([FromRoute] string processingCostCode)
    {
        return Ok(_qualidadeService.GetAllByProcessingCostKey(processingCostCode));
    }
    
}
