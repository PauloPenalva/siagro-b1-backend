using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class SalesContractsPriceFixationsController(
    SalesContractsPriceFixationsUpdateService updateService,
    SalesContractsPriceFixationsGetService getService
    )
    : ODataController
{
    /// <summary>
    /// Violação de regra de negócio é 400, não 500. As guardas de fixação (saldo excedido,
    /// fixação imutável, contrato não aprovado) lançam ApplicationException; exceções não
    /// previstas continuam 500. Espelha o controller de compra.
    /// </summary>
    private ActionResult MapException(Exception ex)
    {
        if (ex is KeyNotFoundException or NotFoundException)
            return NotFound(ex.Message);

        if (ex is DefaultException or BusinessException or ApplicationException)
            return BadRequest(ex.Message);

        return StatusCode(500, ex.Message);
    }

    // A criação/exclusão de fixação são OData actions (SalesContractsPriceFixationCreate/Delete),
    // não POST/DELETE na navegação — assim o frontend as invoca pelo ODataModel e a tela
    // atualiza sem recarregar a rota.

    [HttpPut("odata/SalesContracts({parentKey:guid})/PriceFixations({associationKey:guid})")]
    [HttpPut("odata/SalesContracts/{parentKey:guid}/PriceFixations/{associationKey:guid}")]
    public async Task<IActionResult> UpdatePriceFixationsAsync(
        [FromRoute] Guid parentKey,
        [FromRoute] Guid associationKey,
        [FromBody] SalesContractPriceFixation associationEntity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(parentKey, associationKey, associationEntity);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }

        return NoContent();
    }

    [HttpGet("odata/SalesContracts({key:guid})/PriceFixations")]
    [HttpGet("odata/SalesContracts/{key:guid}/PriceFixations")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesContractPriceFixation>> GetPriceFixations([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }

    /// <summary>
    /// Fila da diretoria: fixações em aprovação de todos os contratos.
    /// </summary>
    [HttpGet("odata/SalesContractsPriceFixations")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesContractPriceFixation>> GetPending()
    {
        return Ok(getService.QueryPending());
    }

    /// <summary>
    /// GET da fixação por chave na entity set. Necessário para o v4 ler propriedades
    /// tardias (late properties) de um item da fila de aprovação sem casar com o Get de
    /// duas chaves. Espelha o controller de compra.
    /// </summary>
    [HttpGet("odata/SalesContractsPriceFixations({key:guid})")]
    [HttpGet("odata/SalesContractsPriceFixations/{key:guid}")]
    [EnableQuery]
    public async Task<ActionResult<SalesContractPriceFixation>> GetByKey([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [HttpGet("odata/SalesContracts({key:guid})/PriceFixations({fixationKey:guid})")]
    [HttpGet("odata/SalesContracts/{key:guid}/PriceFixations/{fixationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<SalesContractPriceFixation>> Get([FromRoute] Guid key, [FromRoute] Guid fixationKey)
    {
        var item = await getService.GetByIdAsync(key, fixationKey);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<SalesContractPriceFixation> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        SalesContractPriceFixation? t = await getService.GetByIdAsync(key);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(key, t);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }

        return NoContent();
    }
}
