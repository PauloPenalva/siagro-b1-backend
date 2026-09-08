using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class FinancialDocumentsController(FinancialDocumentsGetService getService) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 5)]
    public ActionResult<IEnumerable<FinancialDocument>> Get() => Ok(getService.QueryAll());

    // Rota literal nas DUAS formas: a não declarada toma 404 no UI5.
    [HttpGet("odata/FinancialDocuments({key:guid})")]
    [HttpGet("odata/FinancialDocuments/{key:guid}")]
    [EnableQuery(MaxExpansionDepth = 5)]
    public async Task<ActionResult<FinancialDocument>> Get([FromRoute] Guid key)
    {
        var entity = await getService.GetByIdAsync(key);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpGet("odata/FinancialDocuments({key:guid})/Settlements")]
    [HttpGet("odata/FinancialDocuments/{key:guid}/Settlements")]
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialSettlement>> GetSettlements([FromRoute] Guid key) =>
        Ok(getService.QueryAll().Where(x => x.Key == key).SelectMany(x => x.Settlements));

    [HttpGet("odata/FinancialDocuments({key:guid})/ChangeLogs")]
    [HttpGet("odata/FinancialDocuments/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialDocumentChangeLog>> GetChangeLogs([FromRoute] Guid key) =>
        Ok(getService.QueryAll().Where(x => x.Key == key).SelectMany(x => x.ChangeLogs));
}
