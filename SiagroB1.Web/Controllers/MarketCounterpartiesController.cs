using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.OData.UriParser;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>Corretoras e bancos ($expand=Accounts habilitado). Gravação exige MARKET_MANAGE.</summary>
public class MarketCounterpartiesController(MarketCounterpartyService service, MarketPermissionGuard guard)
    : MarketODataController<MarketCounterparty, int>(service, guard)
{
    /// <summary>
    /// Inclusão em profundidade: o 201 devolve a contraparte COM as contas criadas. Sem <c>$expand</c> na URL o
    /// serializador OData omitiria a navegação; então, se o cliente não pediu, a resposta expande <c>Accounts</c>.
    /// </summary>
    public override Task<IActionResult> Post([FromBody] MarketCounterparty entity)
    {
        ExpandAccountsInResponse();
        return base.Post(entity);
    }

    private void ExpandAccountsInResponse()
    {
        var request = HttpContext?.Request;
        if (request is null || request.Query.ContainsKey("$expand"))
            return;
        var feature = request.ODataFeature();
        var model = feature.Model;
        var entitySet = model?.EntityContainer?.FindEntitySet("MarketCounterparties");
        if (entitySet is null)
            return;

        var parser = new ODataQueryOptionParser(model, entitySet.EntityType, entitySet,
            new Dictionary<string, string> { ["$expand"] = nameof(MarketCounterparty.Accounts) });
        feature.SelectExpandClause = parser.ParseSelectAndExpand();
    }
}
