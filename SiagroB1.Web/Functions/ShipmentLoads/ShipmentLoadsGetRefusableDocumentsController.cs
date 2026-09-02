using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>
/// Documentos de saída da carga que ainda podem ser recusados. Alimenta a grade do diálogo de
/// recusa, com o volume devolvível já pré-calculado por documento.
/// </summary>
public class ShipmentLoadsGetRefusableDocumentsController(
    ShipmentLoadsRefusableDocumentsService service)
    : ODataController
{
    /// <summary>
    /// Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.
    /// </summary>
    [EnableQuery]
    [HttpGet("odata/ShipmentLoadsGetRefusableDocuments(Key={key})")]
    public async Task<ActionResult<IEnumerable<ShipmentLoadRefusableDocumentDto>>> Get(
        [FromRoute] Guid key)
    {
        return Ok(await service.ExecuteAsync(key));
    }
}
