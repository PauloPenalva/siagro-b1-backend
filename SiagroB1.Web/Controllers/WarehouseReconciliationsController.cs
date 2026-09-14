using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WarehouseReconciliationsController(
    WarehouseReconciliationsCreateService createService,
    WarehouseReconciliationsUpdateService updateService,
    WarehouseReconciliationsGetService getService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<WarehouseReconciliation>> Get() => Ok(getService.QueryAll());

    /// <summary>
    /// Devolve SingleResult (e não a entidade materializada) para que o EnableQuery componha
    /// $expand/$select sobre o IQueryable — com a entidade já carregada, o $expand=Reason era
    /// aceito mas voltava "Reason": null e o Motivo ficava em branco no detalhe e na aprovação.
    /// Chave inexistente vira 404 pelo próprio SingleResult vazio.
    /// </summary>
    [EnableQuery]
    public SingleResult<WarehouseReconciliation> Get([FromRoute] Guid key) =>
        SingleResult.Create(getService.QueryAll().Where(x => x.Key == key));

    public async Task<IActionResult> Post([FromBody] WarehouseReconciliation entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(entity);
        }
        catch (Exception ex) when (ex is ApplicationException or DefaultException)
        {
            return BadRequest(ex.Message);
        }
    }

    public Task<IActionResult> Put([FromRoute] Guid key, [FromBody] WarehouseReconciliation entity) =>
        UpdateAsync(key, entity);

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<WarehouseReconciliation> patch)
    {
        var current = await getService.GetByIdAsync(key);
        if (current == null)
            return NotFound();

        patch.Patch(current);
        return await UpdateAsync(key, current);
    }

    public IActionResult Delete([FromRoute] Guid key) =>
        BadRequest("Não é possível excluir uma conferência de saldo. Efetue o cancelamento.");

    private async Task<IActionResult> UpdateAsync(Guid key, WarehouseReconciliation entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await updateService.ExecuteAsync(key, entity, User.Identity?.Name ?? "Unknown");
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ApplicationException or DefaultException)
        {
            return BadRequest(ex.Message);
        }
    }
}
