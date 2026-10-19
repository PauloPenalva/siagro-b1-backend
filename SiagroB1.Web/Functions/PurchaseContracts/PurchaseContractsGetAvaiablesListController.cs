using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsGetAvaiablesListController(
    PurchaseContractsGetService getService
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/PurchaseContractsGetAvaiablesList(CardCode={cardCode},ItemCode={itemCode})")]
    public async Task<ActionResult<IEnumerable<PurchaseContractDto>>> GetAsync(
        [FromRoute] string cardCode, [FromRoute] string itemCode)
    {
        return Ok(await getService.GetAvaiablesPurchaseContractsAsync(cardCode, itemCode));
    }

    /// <summary>
    /// Mesma lista, a partir do romaneio: perna de compra de Expedição recebe só o contrato da
    /// liberação, marcado em <see cref="PurchaseContractDto.IsShippingReleaseContract"/>.
    /// </summary>
    [EnableQuery]
    [HttpGet("odata/PurchaseContractsGetAvaiablesListForTransaction(StorageTransactionKey={storageTransactionKey})")]
    public async Task<ActionResult<IEnumerable<PurchaseContractDto>>> GetForTransactionAsync(
        [FromRoute] Guid storageTransactionKey)
    {
        try
        {
            return Ok(await getService.GetAvaiablesPurchaseContractsForTransactionAsync(storageTransactionKey));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
    
}