using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsGetFiscalComplementController(PurchaseContractFiscalComplementService service)
    : ODataController
{
    /// <summary>Devolve <c>Ok(null)</c> sem linha: contrato sem complemento é o caso comum, não um erro.</summary>
    [HttpGet("odata/PurchaseContractsGetFiscalComplement(Key={key})")]
    public async Task<IActionResult> Get([FromRoute] Guid key)
    {
        var result = await service.GetAsync(key);

        return Ok(result);
    }
}
