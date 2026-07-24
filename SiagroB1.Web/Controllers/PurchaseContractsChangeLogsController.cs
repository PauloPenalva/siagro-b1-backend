using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Log de alterações do contrato de compra: somente leitura. As linhas nascem nos services que
/// fazem a alteração — não há endpoint de escrita, de propósito.
/// </summary>
public class PurchaseContractsChangeLogsController(
    PurchaseContractsChangeLogsGetService getService)
    : ODataController
{
    [HttpGet("odata/PurchaseContracts({key:guid})/ChangeLogs")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractChangeLog>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
