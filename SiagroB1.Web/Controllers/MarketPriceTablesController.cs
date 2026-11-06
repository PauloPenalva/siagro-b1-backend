using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.OData.UriParser;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>Cabeçalho da tabela de preços ($expand=Lines habilitado). Gravação exige MARKET_MANAGE.</summary>
public class MarketPriceTablesController(MarketPriceTableService service, MarketPermissionGuard guard)
    : MarketODataController<MarketPriceTable, int>(service, guard)
{
    protected override Task<MarketPriceTable> AddAsync(MarketPriceTable entity) => service.CreateAsync(entity, UserName);

    protected override Task SaveAsync(int key, MarketPriceTable entity) => service.UpdateAsync(key, entity, UserName);

    /// <summary>
    /// Inclusão em profundidade: o 201 devolve o cabeçalho COM as linhas criadas (o OpenUI5 lê as linhas da resposta
    /// do deep create). Sem <c>$expand</c> na URL, o serializador OData omitiria a navegação; então, se o cliente não
    /// pediu, a resposta expande <c>Lines</c>.
    /// </summary>
    public override Task<IActionResult> Post([FromBody] MarketPriceTable entity)
    {
        ExpandLinesInResponse();
        return base.Post(entity);
    }

    private void ExpandLinesInResponse()
    {
        var request = HttpContext?.Request;
        if (request is null || request.Query.ContainsKey("$expand"))
            return;
        var feature = request.ODataFeature();
        var model = feature.Model;
        var entitySet = model?.EntityContainer?.FindEntitySet("MarketPriceTables");
        if (entitySet is null)
            return;

        var parser = new ODataQueryOptionParser(model, entitySet.EntityType, entitySet,
            new Dictionary<string, string> { ["$expand"] = nameof(MarketPriceTable.Lines) });
        feature.SelectExpandClause = parser.ParseSelectAndExpand();
    }
}
