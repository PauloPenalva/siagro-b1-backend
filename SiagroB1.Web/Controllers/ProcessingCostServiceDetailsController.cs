using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class ProcessingCostServiceDetailsController(IProcessingCostServiceDetailService servicoServiceDetailService) 
    : ODataBaseController<ProcessingCostServiceDetail, Guid>(servicoServiceDetailService)
{
    protected readonly IProcessingCostServiceDetailService ServicoServiceDetailService = servicoServiceDetailService;
        
    [HttpPost("odata/ProcessingCosts({tabelaCustoId})/ServiceDetails")]
    public virtual async Task<IActionResult> PostAsync([FromODataUri] Guid tabelaCustoId, [FromBody] ProcessingCostServiceDetail entity)
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
    [HttpGet("odata/ProcessingCosts/{tabelaCustoId}/ServiceDetails({key})")]
    [HttpGet("odata/ProcessingCosts({tabelaCustoId})/ServiceDetails({key})")]
    public virtual async Task<IActionResult> GetAsync([FromODataUri] Guid tabelaCustoId, [FromODataUri] Guid key)
    {
        var item = await ServicoServiceDetailService.FindByKeyAsync(tabelaCustoId, key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);

    }

    [HttpGet("odata/ProcessingCosts({tabelaCustoId})/ServiceDetails")]
    [EnableQuery]
    public ActionResult<IQueryable<ProcessingCostDryingDetail>> GetServicos([FromRoute] Guid tabelaCustoId)
    {
        return Ok(ServicoServiceDetailService.GetAllByProcessingCostKey(tabelaCustoId));
    }
}