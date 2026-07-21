using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsPriceFixationsController(
    PurchaseContractsPriceFixationsUpdateService updateService,
    PurchaseContractsPriceFixationsGetService getService
    )
    : ODataController
{
    /// <summary>
    /// Violação de regra de negócio é 400, não 500. As guardas de fixação
    /// (saldo excedido, fixação imutável, contrato não aprovado) lançam
    /// ApplicationException; sem este mapeamento elas chegavam ao usuário como
    /// erro técnico, e o handler global de OData do frontend as tratava como falha
    /// de sistema em vez de mostrar a mensagem de negócio.
    /// Exceções não previstas continuam 500 — mascarar tudo como 400 esconderia
    /// falhas reais de infraestrutura.
    /// </summary>
    private ActionResult MapException(Exception ex)
    {
        if (ex is KeyNotFoundException or NotFoundException)
            return NotFound(ex.Message);

        if (ex is DefaultException or BusinessException or ApplicationException)
            return BadRequest(ex.Message);

        return StatusCode(500, ex.Message);
    }

    // A criação de fixação é uma OData action (PurchaseContractsPriceFixationCreate,
    // ver PurchaseContractsPriceFixationCreateController), não o POST na navegação —
    // assim o frontend a invoca pelo ODataModel e a tela atualiza sem recarregar a rota.

    [HttpPut("odata/PurchaseContracts({parentKey:guid})/PriceFixations({associationKey:guid})")]
    [HttpPut("odata/PurchaseContracts/{parentKey:guid}/PriceFixations/{associationKey:guid}")]
    public async Task<IActionResult> UpdatePriceFixationsAsync(
        [FromRoute] Guid parentKey, 
        [FromRoute] Guid associationKey,
        [FromBody] PurchaseContractPriceFixation associationEntity)
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
    
    // A exclusão de fixação é uma OData action (PurchaseContractsPriceFixationDelete,
    // ver PurchaseContractsPriceFixationDeleteController), não o DELETE na navegação —
    // assim o frontend a invoca pelo ODataModel e a linha some sem recarregar a rota.

    [HttpGet("odata/PurchaseContracts({key:guid})/PriceFixations")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/PriceFixations")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractPriceFixation>> GetPriceFixations([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }

    /// <summary>
    /// Fila da diretoria: fixações em aprovação de todos os contratos.
    /// </summary>
    [HttpGet("odata/PurchaseContractsPriceFixations")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractPriceFixation>> GetPending()
    {
        return Ok(getService.QueryPending());
    }

    /// <summary>
    /// GET da fixação por chave na entity set. Necessário para o v4 ler propriedades
    /// tardias (late properties) de um item da fila de aprovação — ex.: ao abrir o
    /// diálogo de detalhes, o modelo busca FreightCost/PaymentDetails/ApprovalComments
    /// que não estavam no $select da lista. Sem este handler, o roteamento convencional
    /// do OData casava a URL de chave única com o Get de duas chaves (contrato+fixação),
    /// interpretava a chave da fixação como chave de contrato e devolvia 500.
    /// </summary>
    [HttpGet("odata/PurchaseContractsPriceFixations({key:guid})")]
    [HttpGet("odata/PurchaseContractsPriceFixations/{key:guid}")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractPriceFixation>> GetByKey([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpGet("odata/PurchaseContracts({key:guid})/PriceFixations({fixationKey:guid})")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/PriceFixations/{fixationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractPriceFixation>> Get([FromRoute] Guid key, [FromRoute] Guid fixationKey)
    {
        var item = await getService.GetByIdAsync(key, fixationKey);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<PurchaseContractPriceFixation> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        PurchaseContractPriceFixation? t = await getService.GetByIdAsync(key);

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