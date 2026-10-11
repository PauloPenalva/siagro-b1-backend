using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.StorageDeposits;

/// <summary>Resumo da remessa para depósito na liberação.</summary>
public class ShipmentReleasesStorageRemittanceSummaryController(ShipmentReleasesStorageRemittanceSummaryService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [HttpGet("odata/ShipmentReleasesStorageRemittanceSummary(Key={key})")]
    public async Task<ActionResult<StorageRemittanceSummaryDto>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
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
