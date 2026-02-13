using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class ProcessingCostServiceDetailsController(
    IProcessingCostServiceDetailService servicoServiceDetailService,
    ILogger<ProcessingCostServiceDetailsController> logger
    ) : ODataBaseController<ProcessingCostServiceDetail, int>(servicoServiceDetailService)
{
    protected readonly IProcessingCostServiceDetailService ServicoServiceDetailService = servicoServiceDetailService;
        
    [HttpPost("odata/ProcessingCosts({processingCostCode})/ServiceDetails")]
    public virtual async Task<IActionResult> CreateServiceDetailsAsync([FromODataUri] string processingCostCode, [FromBody] ProcessingCostServiceDetail entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await ServicoServiceDetailService.CreateAsync(processingCostCode, entity);

            return Created(entity);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }
            
            logger.LogError(ex.Message, ex);
            return StatusCode(500, ex.Message);
        }

    }

    // quando se esta alterando a entidade pai, apos incluir um item de linha, a OpenUI5 esta chamando neste formato a requisição
    // fora de padrão
    [HttpGet("odata/ProcessingCosts/{processingCostCode}/ServiceDetails({itemId})")]
    [HttpGet("odata/ProcessingCosts({processingCostCode})/ServiceDetails({itemId})")]
    public virtual async Task<IActionResult> GetServiceDetailsAsync([FromODataUri] string processingCostCode, [FromODataUri] int itemId)
    {
        var item = await ServicoServiceDetailService.FindByKeyAsync(processingCostCode, itemId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);

    }

    [HttpGet("odata/ProcessingCosts({processingCostCode})/ServiceDetails")]
    [EnableQuery]
    public ActionResult<IQueryable<ProcessingCostDryingDetail>> GetSServiceDetails([FromRoute] string processingCostCode)
    {
        return Ok(ServicoServiceDetailService.GetAllByProcessingCostKey(processingCostCode));
    }
}