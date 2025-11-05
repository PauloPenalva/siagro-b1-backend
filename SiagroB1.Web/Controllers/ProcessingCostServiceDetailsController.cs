using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class ProcessingCostServiceDetailsController(IProcessingCostServiceDetailService servicoServiceDetailService) 
    : ODataBaseController<ProcessingCostServiceDetail, int>(servicoServiceDetailService)
{
    protected readonly IProcessingCostServiceDetailService ServicoServiceDetailService = servicoServiceDetailService;
        
    [HttpPost("odata/ProcessingCosts({tabelaCustoId})/ServiceDetails")]
    public virtual async Task<IActionResult> PostAsync([FromODataUri] string tabelaCustoId, [FromBody] ProcessingCostServiceDetail entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await ServicoServiceDetailService.CreateAsync(tabelaCustoId, entity);

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
    [HttpGet("odata/ProcessingCosts/{tabelaCustoId}/ServiceDetails({itemId})")]
    [HttpGet("odata/ProcessingCosts({tabelaCustoId})/ServiceDetails({itemId})")]
    public virtual async Task<IActionResult> GetAsync([FromODataUri] string tabelaCustoId, [FromODataUri] int itemId)
    {
        var item = await ServicoServiceDetailService.FindByKeyAsync(tabelaCustoId, itemId);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);

    }

    [HttpGet("odata/ProcessingCosts({tabelaCustoId})/ServiceDetails")]
    [EnableQuery]
    public ActionResult<IQueryable<ProcessingCostDryingDetail>> GetServicos([FromRoute] string tabelaCustoId)
    {
        return Ok(ServicoServiceDetailService.GetAllByProcessingCostKey(tabelaCustoId));
    }
}