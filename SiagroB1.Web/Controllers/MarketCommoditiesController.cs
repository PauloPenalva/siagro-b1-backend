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
/// Commodities da posição (chave string: dentro do $batch a chave chega codificada, por isso toda action com chave
/// normaliza antes). O 201 devolve a commodity com itens e meses.
/// </summary>
public class MarketCommoditiesController(MarketCommodityService service, MarketPermissionGuard guard)
    : MarketODataController<MarketCommodity, string>(service, guard)
{
    protected override string KeyPropertyName => nameof(MarketCommodity.Code);

    [EnableQuery]
    public override Task<ActionResult<MarketCommodity>> Get([FromRoute] string key) =>
        base.Get(ODataKeyNormalizer.Normalize(key));

    public override Task<IActionResult> Put([FromRoute] string key, [FromBody] MarketCommodity entity) =>
        base.Put(ODataKeyNormalizer.Normalize(key), entity);

    public override Task<IActionResult> Delete([FromRoute] string key) =>
        base.Delete(ODataKeyNormalizer.Normalize(key));

    [AcceptVerbs("PATCH", "MERGE")]
    public override Task<IActionResult> Patch([FromODataUri] string key, [FromBody] Delta<MarketCommodity> patch) =>
        base.Patch(ODataKeyNormalizer.Normalize(key), patch);

    /// <summary>Inclusão em profundidade: sem $expand na URL, a resposta expande Items e Months.</summary>
    public override Task<IActionResult> Post([FromBody] MarketCommodity entity)
    {
        ExpandChildrenInResponse();
        return base.Post(entity);
    }

    private void ExpandChildrenInResponse()
    {
        var request = HttpContext?.Request;
        if (request is null || request.Query.ContainsKey("$expand"))
            return;
        var feature = request.ODataFeature();
        var model = feature.Model;
        var entitySet = model?.EntityContainer?.FindEntitySet("MarketCommodities");
        if (entitySet is null)
            return;

        var parser = new ODataQueryOptionParser(model, entitySet.EntityType, entitySet,
            new Dictionary<string, string> { ["$expand"] = $"{nameof(MarketCommodity.Items)},{nameof(MarketCommodity.Months)}" });
        feature.SelectExpandClause = parser.ParseSelectAndExpand();
    }
}
