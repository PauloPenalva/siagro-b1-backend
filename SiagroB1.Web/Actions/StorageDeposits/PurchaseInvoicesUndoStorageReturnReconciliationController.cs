using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageDeposits;

/// <summary>"Desfazer reconciliação": solta um romaneio do retorno de depósito.</summary>
public class PurchaseInvoicesUndoStorageReturnReconciliationController(StorageReturnReconciliationsService service) : ODataController
{
    [HttpPost("odata/PurchaseInvoicesUndoStorageReturnReconciliation")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null
            || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key)
            || !parameters.TryGetValue("TransactionKey", out var txObj) || !Guid.TryParse(txObj?.ToString(), out var transactionKey))
            return BadRequest("Parâmetros obrigatórios: Key, TransactionKey.");

        try
        {
            await service.UndoAsync(key, transactionKey);
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
