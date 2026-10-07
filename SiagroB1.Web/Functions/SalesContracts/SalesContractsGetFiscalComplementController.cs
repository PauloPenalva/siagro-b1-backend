using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Application.Services.SalesContracts;

namespace SiagroB1.Web.Functions.SalesContracts;

public class SalesContractsGetFiscalComplementController(SalesContractFiscalComplementService service)
    : ODataController
{
    /// <summary>Devolve <c>Ok(null)</c> sem linha: contrato sem complemento é o caso comum, não um erro (ver ItemsGetComplement).</summary>
    [HttpGet("odata/SalesContractsGetFiscalComplement(Key={key})")]
    public async Task<IActionResult> Get([FromRoute] Guid key)
    {
        var result = await service.GetAsync(key);

        return Ok(result);
    }
}
