using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Limites de exposição: leitura livre; toda gravação exige MARKET_BOARD_APPROVE (diretoria), conferida de novo no
/// serviço, que também carimba quem gravou e quando.
/// </summary>
public class MarketExposureLimitsController(MarketExposureLimitService service, MarketPermissionGuard guard)
    : MarketODataController<MarketExposureLimit, int>(service, guard)
{
    protected override string WritePermission => PermissionCodes.MarketBoardApprove;

    protected override Task<MarketExposureLimit> AddAsync(MarketExposureLimit entity) => service.CreateAsync(entity, UserName);

    protected override Task SaveAsync(int key, MarketExposureLimit entity) => service.UpdateAsync(key, entity, UserName);

    protected override Task<bool> RemoveAsync(int key) => service.DeleteAsync(key, UserName);
}
