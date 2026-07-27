using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Comentários do documento de saída: somente leitura aqui. A escrita passa pelas actions
/// SalesInvoicesComment{Create,Update,Delete}, que carimbam o autor e gravam o log de alterações na
/// mesma transação.
/// </summary>
public class SalesInvoicesCommentsController(
    SalesInvoicesCommentsGetService getService)
    : ODataController
{
    [HttpGet("odata/SalesInvoices({key:guid})/CommentEntries")]
    [HttpGet("odata/SalesInvoices/{key:guid}/CommentEntries")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceComment>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
