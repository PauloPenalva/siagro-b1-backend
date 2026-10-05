using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

/// <summary>Itens da venda com o saldo devolvível — a grade do diálogo "Devolver".</summary>
public class SalesInvoicesNfeReturnableItemsController(SalesInvoicesNfeReturnableItemsService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [EnableQuery]
    [HttpGet("odata/SalesInvoicesNfeReturnableItems(Key={key})")]
    public async Task<ActionResult<IEnumerable<SalesInvoiceNfeReturnableItemDto>>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
