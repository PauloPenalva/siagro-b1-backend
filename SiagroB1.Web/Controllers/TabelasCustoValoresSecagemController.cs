using SiagroB1.Domain.Entities;
using SiagroB1.Core.Interfaces;
using SiagroB1.Web.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Domain.Exceptions;
using Microsoft.AspNetCore.OData.Query;
// ReSharper disable All

namespace SiagroB1.Web.Controllers
{
    public class TabelasCustoValoresSecagemController(ITabelaCustoValorSecagemService valorSecagemService) 
        : ODataBaseController<TabelaCustoValorSecagem, int>(valorSecagemService)
    {
        protected readonly ITabelaCustoValorSecagemService _valorSecagemService = valorSecagemService;
        
        [HttpPost("odata/TabelasCusto({tabelaCustoId})/ValoresSecagem")]
        public virtual async Task<IActionResult> PostAsync([FromODataUri] int tabelaCustoId, [FromBody] TabelaCustoValorSecagem entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _valorSecagemService.CreateAsync(tabelaCustoId, entity);

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
        [HttpGet("odata/TabelasCusto/{tabelaCustoId}/ValoresSecagem({key})")]
        [HttpGet("odata/TabelasCusto({tabelaCustoId})/ValoresSecagem({key})")]
        public virtual async Task<IActionResult> GetAsync([FromODataUri] int tabelaCustoId, [FromODataUri] int key)
        {
            var item = await _valorSecagemService.FindByKeyAsync(tabelaCustoId, key);

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);

        }

        [HttpGet("odata/TabelasCusto({tabelaCustoId})/ValoresSecagem")]
        [EnableQuery]
        public ActionResult<IQueryable<TabelaCustoValorSecagem>> GetValoresSecagem([FromRoute] int tabelaCustoId)
        {
            return Ok(_valorSecagemService.GetAllByTabelaCustoId(tabelaCustoId));
        }
        
        
    }
}