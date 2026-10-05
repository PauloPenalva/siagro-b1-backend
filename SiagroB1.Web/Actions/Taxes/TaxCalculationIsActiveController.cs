using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Taxes;

namespace SiagroB1.Web.Actions.Taxes;

/// <summary>
/// A tela pergunta ao servidor se a filial do documento calcula tributos, em vez de juntar o
/// modo e a chave por conta própria: a regra continua decidida num lugar só.
/// </summary>
public class TaxCalculationIsActiveController(TaxCalculationGate gate) : ODataController
{
    [HttpGet("odata/TaxCalculationIsActive(BranchCode={branchCode})")]
    public async Task<IActionResult> GetAsync([FromRoute] string branchCode)
    {
        // Rotas por atributo entregam o segmento COM as aspas simples do OData.
        return Ok(await gate.IsActiveAsync(branchCode?.Trim('\'')));
    }
}
