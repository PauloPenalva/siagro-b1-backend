using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.PurchaseContracts;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsGetAvaiablesListController(
    PurchaseContractsGetService getService
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/PurchaseContractsGetAvaiablesList(CardCode={cardCode},ItemCode={itemCode})")]
    public ActionResult<IEnumerable<PurchaseContractDto>> GetAsync(
        [FromRoute] string cardCode, [FromRoute] string itemCode)
    {
        return Ok(getService.GetAvaiablesPurchaseContracts(cardCode, itemCode));
    }
    
}