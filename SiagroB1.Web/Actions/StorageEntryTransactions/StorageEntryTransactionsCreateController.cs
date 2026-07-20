using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageEntryTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageEntryTransactions;

public class StorageEntryTransactionsCreateController(
    StorageEntryTransactionsCreateService service) : ODataController
{
    [HttpPost("odata/StorageEntryTransactionsCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            var purchaseContractKey = (Guid) parameters["PurchaseContractKey"];
            var storageAddressCode = (string) parameters["StorageAddressCode"];
            var storageTransaction = (StorageTransaction) parameters["StorageTransaction"];

            var entry = await service.ExecuteAsync(
                purchaseContractKey, storageAddressCode, storageTransaction, userName);

            return Ok(new
            {
                entry.Key,
                entry.AllocatedVolume,
                entry.ReceiptNetWeight,
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
