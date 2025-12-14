using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsAllocationCreateController(PurchaseContractsAllocationCreateService service)
: ODataController
{
    [HttpPost("odata/PurchaseContractsCreateAllocation")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("PurchaseContractKey", out var purchaseKeyObj) || 
                !parameters.TryGetValue("StorageTransactionKey", out var storageTransactionKeyObj) ||
                !parameters.TryGetValue("Volume", out var volumeObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var userName = User.Identity?.Name ?? "Unknown";
            var purchaseKey = Guid.Parse(purchaseKeyObj.ToString());
            var storageKey = Guid.Parse(storageTransactionKeyObj.ToString());
            var volume = decimal.Parse(volumeObj.ToString());
            
            await service.ExecuteWithTransactionAsync(purchaseKey, storageKey, volume, userName);
            
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }
            
            return BadRequest(e.Message);
        }
    }
}