using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.StorageCustody;

/// <summary>Notas fiscais de custódia do romaneio.</summary>
public class StorageTransactionsCustodySummaryController(StorageTransactionsCustodySummaryService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [HttpGet("odata/StorageTransactionsCustodySummary(Key={key})")]
    public async Task<ActionResult<StorageCustodySummaryDto>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
