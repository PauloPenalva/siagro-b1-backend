using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class FinancialSettlementsController(FinancialDocumentsGetService getService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialSettlement>> Get() =>
        Ok(getService.QueryAll().SelectMany(x => x.Settlements));
}
