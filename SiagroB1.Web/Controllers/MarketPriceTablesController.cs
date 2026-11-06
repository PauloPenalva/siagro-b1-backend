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
}
