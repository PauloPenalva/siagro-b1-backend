using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class StorageInvoicesController(
    StorageInvoicesGetService service
    ) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<StorageInvoice>> Get()
    {
        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<StorageInvoice>> GetAsync([FromRoute] Guid key)
    {
        var item = await service.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
}