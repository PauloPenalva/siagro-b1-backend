using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services;

namespace SiagroB1.Web.Controllers;

/// <summary>Países — somente leitura, nos dois modos (é dado de referência).</summary>
public class CountriesController(CountryService service) : ODataController
{
    [EnableQuery(PageSize = 300)]
    public IActionResult Get() => Ok(service.QueryAll());

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] string key)
    {
        var country = await service.GetByIdAsync(key.Trim('\''));

        return country is null ? NotFound() : Ok(country);
    }
}
