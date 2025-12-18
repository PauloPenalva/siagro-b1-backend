using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.ShippingTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShippingTransactions;

public class ShippingTransactionsCreateController(ShippingTransactionsCreateService service, ILogger<ShippingTransactionsCreateController> logger)
    :ODataController
{

    [HttpPost("odata/ShippingTransactionsCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            var purchaseContractKey = (Guid) parameters["PurchaseContractKey"];
            var storageTransaction = (StorageTransaction) parameters["StorageTransaction"];
            
            var shippingTransaction = await service.ExecuteAsync(purchaseContractKey, storageTransaction, userName);
            
            return Ok(new 
            {
                shippingTransaction.Key 
            });
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
            {
                return NotFound(e.Message);
            }

            if (e is ApplicationException)
            {
                return BadRequest(e.Message);
            }
            
            return StatusCode(500, e.Message);
        }
    }
}