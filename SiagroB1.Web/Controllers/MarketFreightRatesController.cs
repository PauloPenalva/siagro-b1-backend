using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>Fretes por cidade e destino com vigência. Gravação exige MARKET_MANAGE; a auditoria usa o usuário logado.</summary>
public class MarketFreightRatesController(MarketFreightRateService service, MarketPermissionGuard guard)
    : MarketODataController<MarketFreightRate, int>(service, guard)
{
    protected override Task<MarketFreightRate> AddAsync(MarketFreightRate entity) => service.CreateAsync(entity, UserName);

    protected override Task SaveAsync(int key, MarketFreightRate entity) => service.UpdateAsync(key, entity, UserName);
}
