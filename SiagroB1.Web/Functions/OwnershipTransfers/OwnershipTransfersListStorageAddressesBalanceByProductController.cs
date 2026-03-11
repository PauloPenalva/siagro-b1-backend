using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.OwnershipTransfers;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.OwnershipTransfers;

public class OwnershipTransfersListStorageAddressesBalanceByProductController(
    OwnershipTransfersListStorageAddressesBalanceByProductService service
    ) : ODataController
{
    [HttpGet("odata/OwnershipTransfersListStorageAddressesBalanceByProduct(ItemCode={itemCode},IgnoreCode={ignoreCode})")]
    public async Task<ActionResult<ICollection<PurchaseContractAttachmentsDto>>> List([FromRoute] string itemCode, string? ignoreCode)
    {
        return Ok(await service.ExecuteAsync(itemCode, ignoreCode));
    }
}