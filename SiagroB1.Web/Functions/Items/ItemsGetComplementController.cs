using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Functions.Items;

public class ItemsGetComplementController(IItemComplementService service)
    : ODataController
{
    /// <summary>
    /// Devolve <c>Ok(null)</c> — não <c>NotFound</c> — quando o item ainda não tem complemento,
    /// que é o caso comum. Um 404 aqui viraria mensagem de erro global (ver <c>Component.ts</c>,
    /// que exibe qualquer OData technical message que não seja 401), e a ausência de configuração
    /// não é um erro para esta tela.
    /// </summary>
    [HttpGet("odata/ItemsGetComplement(ItemCode={itemCode})")]
    public async Task<IActionResult> Get([FromRoute] string itemCode)
    {
        var result = await service.GetAsync(itemCode);

        return Ok(result);
    }
}
