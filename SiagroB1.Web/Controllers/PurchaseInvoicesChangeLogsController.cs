using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Log de alterações do documento de entrada: somente leitura. Quem escreve é
/// <c>PurchaseInvoicesChangeLogService</c>, sempre no mesmo SaveChanges da alteração que descreve.
/// </summary>
public class PurchaseInvoicesChangeLogsController(
    PurchaseInvoicesChangeLogsGetService getService)
    : ODataController
{
    [HttpGet("odata/PurchaseInvoices({key:guid})/ChangeLogs")]
    [HttpGet("odata/PurchaseInvoices/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseInvoiceChangeLog>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
