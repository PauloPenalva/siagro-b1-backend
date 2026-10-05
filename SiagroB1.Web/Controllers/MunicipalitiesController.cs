using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services;

namespace SiagroB1.Web.Controllers;

/// <summary>Municípios do IBGE — somente leitura, nos dois modos (é dado de referência).</summary>
public class MunicipalitiesController(MunicipalityService service) : ODataController
{
    [EnableQuery(PageSize = 200)]
    public IActionResult Get() => Ok(service.QueryAll());

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] string key)
    {
        var municipality = await service.GetByIdAsync(key.Trim('\''));

        return municipality is null ? NotFound() : Ok(municipality);
    }
}
