using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;
// ReSharper disable All

namespace SiagroB1.Web.Controllers;

public class TabelasCustoServicosController(ITabelaCustoServicoService servicoService) 
    : ODataBaseController<TabelaCustoServico, int>(servicoService)
{
    protected readonly ITabelaCustoServicoService _servicoService = servicoService;
        
    [HttpPost("odata/TabelasCusto({tabelaCustoId})/Servicos")]
    public virtual async Task<IActionResult> PostAsync([FromODataUri] int tabelaCustoId, [FromBody] TabelaCustoServico entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await _servicoService.CreateAsync(tabelaCustoId, entity);

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
    [HttpGet("odata/TabelasCusto/{tabelaCustoId}/Servicos({key})")]
    [HttpGet("odata/TabelasCusto({tabelaCustoId})/Servicos({key})")]
    public virtual async Task<IActionResult> GetAsync([FromODataUri] int tabelaCustoId, [FromODataUri] int key)
    {
        var item = await _servicoService.FindByKeyAsync(tabelaCustoId, key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);

    }

    [HttpGet("odata/TabelasCusto({tabelaCustoId})/Servicos")]
    [EnableQuery]
    public ActionResult<IQueryable<TabelaCustoValorSecagem>> GetServicos([FromRoute] int tabelaCustoId)
    {
        return Ok(_servicoService.GetAllByTabelaCustoId(tabelaCustoId));
    }
}