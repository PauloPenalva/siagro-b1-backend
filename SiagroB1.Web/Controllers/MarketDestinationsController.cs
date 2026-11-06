using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>Destinos do Mercado (porto, indústria). Gravação exige MARKET_MANAGE.</summary>
public class MarketDestinationsController(MarketDestinationService service, MarketPermissionGuard guard)
    : MarketODataController<MarketDestination, int>(service, guard);
