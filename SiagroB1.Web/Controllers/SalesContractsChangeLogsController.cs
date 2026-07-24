using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Log de alterações do contrato de venda: somente leitura. As linhas nascem nos services que
/// fazem a alteração — não há endpoint de escrita, de propósito.
/// </summary>
public class SalesContractsChangeLogsController(
    SalesContractsChangeLogsGetService getService)
    : ODataController
{
    [HttpGet("odata/SalesContracts({key:guid})/ChangeLogs")]
    [HttpGet("odata/SalesContracts/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesContractChangeLog>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
