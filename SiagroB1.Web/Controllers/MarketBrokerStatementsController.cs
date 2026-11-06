using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>Extratos diários da corretora ($expand=Account($expand=Counterparty)). Gravação exige MARKET_MANAGE.</summary>
public class MarketBrokerStatementsController(MarketBrokerStatementService service, MarketPermissionGuard guard)
    : MarketODataController<MarketBrokerStatement, int>(service, guard)
{
    protected override Task<MarketBrokerStatement> AddAsync(MarketBrokerStatement entity) => service.CreateAsync(entity, UserName);

    protected override Task SaveAsync(int key, MarketBrokerStatement entity) => service.UpdateAsync(key, entity, UserName);
}
