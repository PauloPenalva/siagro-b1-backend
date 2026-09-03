using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.SalesInvoices;

/// <summary>
/// Romaneios de um documento de saída LEGADO que ainda podem ser devolvidos. Alimenta a grade do
/// diálogo de retorno, onde o operador escolhe quais carretas voltaram.
/// </summary>
public class SalesInvoicesGetReturnableShipmentsController(
    SalesInvoicesReturnableShipmentsService service)
    : ODataController
{
    /// <summary>
    /// Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.
    /// </summary>
    [EnableQuery]
    [HttpGet("odata/SalesInvoicesGetReturnableShipments(Key={key})")]
    public async Task<ActionResult<IEnumerable<SalesInvoiceReturnableShipmentDto>>> Get(
        [FromRoute] Guid key)
    {
        return Ok(await service.ExecuteAsync(key));
    }
}
