using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class ProcessingCostDryingDetailsController(IProcessingCostDryingDetailService service) 
    : ODataBaseController<ProcessingCostDryingDetail, string>(service)
{
    private readonly IProcessingCostDryingDetailService _service = service;
    
    [HttpPost("odata/ProcessingCosts({processingCostCode})/DryingDetails")]
    public virtual async Task<IActionResult> CreateDryingDetailsAsync([FromODataUri] string processingCostCode, [FromBody] ProcessingCostDryingDetail entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await _service.CreateAsync(processingCostCode, entity);

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
    [HttpGet("odata/ProcessingCosts/{processingCostCode}/DryingDetails({itemId})")]
    [HttpGet("odata/ProcessingCosts({processingCostCode})/DryingDetails({itemId})")]
    public virtual async Task<IActionResult> GetDryingDetailsAsync([FromODataUri] string processingCostCode, [FromODataUri] int itemId)
    {
        var item = await _service.FindByKeyAsync(processingCostCode, itemId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);

    }

    [HttpGet("odata/ProcessingCosts({processingCostCode})/DryingDetails")]
    [EnableQuery]
    public ActionResult<IQueryable<ProcessingCostDryingParameter>> GetDryingDetails([FromRoute] string processingCostCode)
    {
        return Ok(_service.GetAllByProcessingCostKey(processingCostCode));
    }

}
