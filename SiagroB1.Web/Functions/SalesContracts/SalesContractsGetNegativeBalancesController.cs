using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.SalesContracts;

public class SalesContractsGetNegativeBalancesController(
    SalesContractsGetNegativeBalancesService service)
    : ODataController
{
    // Duas rotas de propósito: o frontend chama por bindContext().invoke(), que exige
    // binding deferido "(...)" e monta a URL da função sem parâmetros COM os parênteses.
    // A forma sem parênteses fica para chamada direta (curl/Swagger).
    [EnableQuery]
    [HttpGet("odata/SalesContractsGetNegativeBalances")]
    [HttpGet("odata/SalesContractsGetNegativeBalances()")]
    public async Task<ActionResult<IEnumerable<SalesContractNegativeBalanceDto>>> GetAsync()
    {
        return Ok(await service.ExecuteAsync());
    }
}
