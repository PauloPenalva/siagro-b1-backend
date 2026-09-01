using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Functions.Warehouses;

public class WarehousesGetOwnComplementsController(IWarehouseComplementService service)
    : ODataController
{
    /// <summary>
    /// Armazéns marcados como próprios no complemento cadastral. Usada pelo value help de contrato
    /// de compra da transferência de titularidade, que filtra os contratos pelo local de entrega.
    ///
    /// Duas rotas porque o UI5 chama a função com os parênteses vazios e o roteamento por convenção
    /// não cobre função sem parâmetro. Lista vazia é resposta normal (nenhum armazém marcado), nunca
    /// <c>NotFound</c> — um 404 aqui viraria mensagem de erro global (ver <c>Component.ts</c>).
    /// </summary>
    [HttpGet("odata/WarehousesGetOwnComplements()")]
    [HttpGet("odata/WarehousesGetOwnComplements")]
    public async Task<IActionResult> Get()
    {
        var result = await service.GetOwnAsync();

        return Ok(result);
    }
}
