using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.StorageDeposits;

/// <summary>Romaneios candidatos à reconciliação com o retorno de depósito — a grade do diálogo.</summary>
public class PurchaseInvoicesStorageReturnTransactionsController(StorageReturnReconciliationsService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [EnableQuery]
    [HttpGet("odata/PurchaseInvoicesStorageReturnTransactions(Key={key})")]
    public async Task<ActionResult<IEnumerable<StorageReturnTransactionDto>>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ListAsync(key));
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
