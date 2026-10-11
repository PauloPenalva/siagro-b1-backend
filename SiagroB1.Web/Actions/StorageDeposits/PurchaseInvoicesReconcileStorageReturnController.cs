using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageDeposits;

/// <summary>"Reconciliar": liga o retorno de depósito aos romaneios selecionados (spec 2026-10-10 §3.5).</summary>
public class PurchaseInvoicesReconcileStorageReturnController(StorageReturnReconciliationsService service) : ODataController
{
    [HttpPost("odata/PurchaseInvoicesReconcileStorageReturn")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        var transactionKeys = (parameters.TryGetValue("TransactionKeys", out var keysObj) ? keysObj as IEnumerable<Guid> : null)?.ToList();
        if (transactionKeys is null)
            return BadRequest("Parâmetro obrigatório: TransactionKeys.");

        try
        {
            await service.ReconcileAsync(key, transactionKeys, User.Identity?.Name ?? "Unknown");
            return NoContent();
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
