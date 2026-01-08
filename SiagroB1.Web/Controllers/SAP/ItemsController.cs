using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Interfaces.SAP;

namespace SiagroB1.Web.Controllers.SAP;

public class ItemsController(IItemService service) : ODataController
{ 
    [EnableQuery]
    public virtual ActionResult Get()
    {
        return Ok(service.QueryAll());
    }
    
    [EnableQuery]
    public virtual async Task<ActionResult> Get([FromRoute] string key)
    {
        var item = await service.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
}