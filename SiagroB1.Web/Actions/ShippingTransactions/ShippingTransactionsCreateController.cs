using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShippingTransactions;
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
            // Opcional: o embarque de uma liberação emitida por transferência de
            // titularidade não tem contrato a informar. Leitura defensiva porque o
            // parâmetro ausente vem como chave inexistente, e presente-mas-vazio como null.
            parameters.TryGetValue("PurchaseContractKey", out var purchaseContractKeyObj);
            var purchaseContractKey = purchaseContractKeyObj as Guid?;
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