using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.UriParser;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Conjunto próprio (<c>MarketCommodityItems</c>) e navegação <c>MarketCommodities('code')/Items</c>. O código da commodity vem
/// na rota com aspas e, no $batch, codificado: normaliza antes de usar.
/// </summary>
public class MarketCommodityItemsController(MarketCommodityItemService service, MarketPermissionGuard guard)
    : MarketODataController<MarketCommodityItem, int>(service, guard)
{
    [HttpPost("odata/MarketCommodities({commodityCode})/Items")]
    public Task<IActionResult> PostNested([FromODataUri] string commodityCode, [FromBody] MarketCommodityItem entity)
    {
        entity.CommodityCode = ODataKeyNormalizer.Normalize(commodityCode);
        return Post(entity);
    }

    [HttpGet("odata/MarketCommodities({commodityCode})/Items")]
    [EnableQuery]
    public ActionResult<IQueryable<MarketCommodityItem>> GetNested([FromODataUri] string commodityCode) =>
        Ok(service.QueryByCommodity(ODataKeyNormalizer.Normalize(commodityCode)));

    [HttpGet("odata/MarketCommodities({commodityCode})/Items({id})")]
    public async Task<IActionResult> GetNestedByKey([FromODataUri] string commodityCode, [FromODataUri] int id)
    {
        var entity = await service.GetByIdAsync(id);
        return entity is null || entity.CommodityCode != ODataKeyNormalizer.Normalize(commodityCode) ? NotFound() : Ok(entity);
    }

    [HttpPatch("odata/MarketCommodities({commodityCode})/Items({id})")]
    public async Task<IActionResult> PatchNested(
        [FromODataUri] string commodityCode, [FromODataUri] int id, [FromBody] Delta<MarketCommodityItem> patch) =>
        await BelongsToAsync(commodityCode, id) ? await Patch(id, patch) : NotFound();

    [HttpDelete("odata/MarketCommodities({commodityCode})/Items({id})")]
    public async Task<IActionResult> DeleteNested([FromODataUri] string commodityCode, [FromODataUri] int id) =>
        await BelongsToAsync(commodityCode, id) ? await Delete(id) : NotFound();

    private async Task<bool> BelongsToAsync(string commodityCode, int id) =>
        (await service.GetByIdAsync(id))?.CommodityCode == ODataKeyNormalizer.Normalize(commodityCode);
}
