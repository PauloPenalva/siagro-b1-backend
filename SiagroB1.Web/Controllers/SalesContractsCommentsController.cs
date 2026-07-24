using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Comentários do contrato de venda: somente leitura aqui. A escrita passa pelas actions
/// SalesContractsComment{Create,Update,Delete}, que carimbam o autor e gravam o log de alterações
/// na mesma transação.
/// </summary>
public class SalesContractsCommentsController(
    SalesContractsCommentsGetService getService)
    : ODataController
{
    [HttpGet("odata/SalesContracts({key:guid})/CommentEntries")]
    [HttpGet("odata/SalesContracts/{key:guid}/CommentEntries")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesContractComment>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
