using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Comentários do contrato de compra: somente leitura aqui. A escrita passa pelas actions
/// PurchaseContractsComment{Create,Update,Delete}, que carimbam o autor e gravam o log de
/// alterações na mesma transação.
/// </summary>
public class PurchaseContractsCommentsController(
    PurchaseContractsCommentsGetService getService)
    : ODataController
{
    [HttpGet("odata/PurchaseContracts({key:guid})/CommentEntries")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/CommentEntries")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractComment>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
