using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Functions.Warehouses;

public class WarehousesGetComplementController(IWarehouseComplementService service)
    : ODataController
{
    /// <summary>
    /// Devolve <c>Ok(null)</c> — não <c>NotFound</c> — quando o armazém ainda não tem complemento,
    /// que é o caso comum. Um 404 aqui viraria mensagem de erro global (ver <c>Component.ts</c>),
    /// e a ausência de configuração não é um erro para esta tela.
    /// </summary>
    [HttpGet("odata/WarehousesGetComplement(WarehouseCode={warehouseCode})")]
    public async Task<IActionResult> Get([FromRoute] string warehouseCode)
    {
        var result = await service.GetAsync(warehouseCode);

        return Ok(result);
    }
}
