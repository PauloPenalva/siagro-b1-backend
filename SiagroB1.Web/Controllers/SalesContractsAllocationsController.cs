using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class SalesContractsAllocationsController(
    SalesContractsAllocationGetService allocationGetService
    )
    : ODataController
{
    // GAC-1216: MaxExpansionDepth = 3 porque a coluna "Carga" da lista expande
    // SalesInvoiceItem/SalesInvoice/ShipmentLoad; com o padrão (2) a tela inteira responde 400.
    [EnableQuery(MaxExpansionDepth = 3)]
    public ActionResult<IEnumerable<SalesContractAllocation>> Get()
    {
        return Ok(allocationGetService.QueryAll());
    }

    [EnableQuery(MaxExpansionDepth = 3)]
    public async Task<ActionResult<SalesContractAllocation>> Get([FromRoute] Guid key)
    {
        var item = await allocationGetService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

}
