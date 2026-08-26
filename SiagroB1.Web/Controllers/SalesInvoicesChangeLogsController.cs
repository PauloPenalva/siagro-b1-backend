using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Log de alterações do documento de saída: só leitura. As linhas nascem nos services que fazem a
/// alteração, no mesmo SaveChanges dela.
/// </summary>
public class SalesInvoicesChangeLogsController(
    SalesInvoicesChangeLogsGetService getService)
    : ODataController
{
    /// <summary>
    /// Entity set. Serve o diálogo de log da conferência de entregas, que pede as linhas de um
    /// item via $filter — e não a rota aninhada <c>SalesInvoicesItems(key)/ChangeLogs</c>, que
    /// teria de ser declarada aqui do mesmo jeito.
    /// </summary>
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceChangeLog>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [HttpGet("odata/SalesInvoices({key:guid})/ChangeLogs")]
    [HttpGet("odata/SalesInvoices/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceChangeLog>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
