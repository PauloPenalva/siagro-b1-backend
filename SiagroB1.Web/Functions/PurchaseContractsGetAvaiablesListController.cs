using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Application.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions;

public class PurchaseContractsGetAvaiablesListController(
    PurchaseContractsGetService getService
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/PurchaseContractsGetAvaiablesList(CardCode={cardCode},ItemCode={itemCode})")]
    public ActionResult<ICollection<PurchaseContractDto>> GetAsync(
        [FromRoute] string cardCode, [FromRoute] string itemCode)
    {
        return Ok(getService.GetAvaiablesPurchaseContracts(cardCode, itemCode));
    }
    
}