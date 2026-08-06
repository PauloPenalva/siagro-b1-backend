using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Comentários do documento de entrada: somente leitura aqui. A escrita passa pelas actions
/// PurchaseInvoicesComment{Create,Update,Delete}, que carimbam o autor e gravam o log de alterações
/// na mesma transação.
/// </summary>
public class PurchaseInvoicesCommentsController(
    PurchaseInvoicesCommentsGetService getService)
    : ODataController
{
    [HttpGet("odata/PurchaseInvoices({key:guid})/CommentEntries")]
    [HttpGet("odata/PurchaseInvoices/{key:guid}/CommentEntries")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseInvoiceComment>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
