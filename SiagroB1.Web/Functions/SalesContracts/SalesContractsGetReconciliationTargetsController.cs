using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.SalesContracts;

public class SalesContractsGetReconciliationTargetsController(
    SalesContractsGetReconciliationTargetsService service)
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/SalesContractsGetReconciliationTargets(SalesInvoiceItemKey={salesInvoiceItemKey},SourceSalesContractKey={sourceSalesContractKey},IncludeOtherCustomers={includeOtherCustomers})")]
    public async Task<ActionResult<IEnumerable<SalesContractReconciliationTargetDto>>> GetAsync(
        [FromRoute] Guid salesInvoiceItemKey, [FromRoute] Guid sourceSalesContractKey,
        [FromRoute] bool includeOtherCustomers)
    {
        try
        {
            return Ok(await service.ExecuteAsync(
                salesInvoiceItemKey, sourceSalesContractKey, includeOtherCustomers));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
