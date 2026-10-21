using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Dimensions;

namespace SiagroB1.Web.Controllers;

/// <summary>Códigos de centro de custo e de regra por dimensão. Somente leitura.</summary>
public class DimensionCodesController(DimensionCodeQueryService service) : ODataController
{
    [EnableQuery]
    public virtual ActionResult Get()
    {
        return Ok(service.QueryAll());
    }
}
