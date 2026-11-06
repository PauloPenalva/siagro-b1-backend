using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Linhas da tabela de preços. Além do conjunto próprio (<c>MarketPriceTableLines</c>), expõe a navegação
/// <c>MarketPriceTables({id})/Lines</c> no mesmo formato que o OpenUI5 usa nos custos de processamento
/// (inclusão na entidade pai e leitura por chave depois de salvar).
/// </summary>
public class MarketPriceTableLinesController(MarketPriceTableLineService service, MarketPermissionGuard guard)
    : MarketODataController<MarketPriceTableLine, int>(service, guard)
{
    protected override Task<MarketPriceTableLine> AddAsync(MarketPriceTableLine entity) =>
        service.CreateAsync(entity, UserName);

    protected override Task SaveAsync(int key, MarketPriceTableLine entity) => service.UpdateAsync(key, entity, UserName);

    protected override Task<bool> RemoveAsync(int key) => service.DeleteAsync(key, UserName);

    [HttpPost("odata/MarketPriceTables({priceTableId})/Lines")]
    public Task<IActionResult> PostNested([FromODataUri] int priceTableId, [FromBody] MarketPriceTableLine entity)
    {
        entity.PriceTableId = priceTableId;
        return Post(entity);
    }

    [HttpGet("odata/MarketPriceTables({priceTableId})/Lines")]
    [EnableQuery]
    public ActionResult<IQueryable<MarketPriceTableLine>> GetNested([FromODataUri] int priceTableId) =>
        Ok(service.QueryByTable(priceTableId));

    [HttpGet("odata/MarketPriceTables/{priceTableId}/Lines({lineId})")]
    [HttpGet("odata/MarketPriceTables({priceTableId})/Lines({lineId})")]
    public async Task<IActionResult> GetNestedByKey([FromODataUri] int priceTableId, [FromODataUri] int lineId)
    {
        var line = await service.GetByIdAsync(lineId);
        return line is null || line.PriceTableId != priceTableId ? NotFound() : Ok(line);
    }

    [HttpPatch("odata/MarketPriceTables({priceTableId})/Lines({lineId})")]
    public Task<IActionResult> PatchNested(
        [FromODataUri] int priceTableId, [FromODataUri] int lineId, [FromBody] Delta<MarketPriceTableLine> patch) =>
        Patch(lineId, patch);

    [HttpDelete("odata/MarketPriceTables({priceTableId})/Lines({lineId})")]
    public Task<IActionResult> DeleteNested([FromODataUri] int priceTableId, [FromODataUri] int lineId) => Delete(lineId);
}
