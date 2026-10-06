using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// CC-e do documento de saída: só leitura (as linhas nascem no envio e na consulta). A rota aninhada é
/// declarada aqui porque a navegação OData não ganha rota sozinha — é a que a tabela do detalhe usa.
/// </summary>
public class SalesInvoicesNfeCorrectionsController(SalesInvoicesNfeCorrectionsGetService getService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceNfeCorrection>> Get() => Ok(getService.QueryAll());

    [HttpGet("odata/SalesInvoices({key:guid})/NfeCorrections")]
    [HttpGet("odata/SalesInvoices/{key:guid}/NfeCorrections")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceNfeCorrection>> Get([FromRoute] Guid key) => Ok(getService.QueryAll(key));
}
