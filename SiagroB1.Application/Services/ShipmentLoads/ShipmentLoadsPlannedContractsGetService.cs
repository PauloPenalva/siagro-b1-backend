using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura dos contratos planejados de uma carga (GAC-1185). Devolve
/// <see cref="IQueryable{T}"/> para o OData aplicar $expand=Contract/$filter/$orderby.
/// </summary>
public class ShipmentLoadsPlannedContractsGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadPlannedPurchaseContract> QueryPurchase(Guid shipmentLoadKey) =>
        context.ShipmentLoadsPlannedPurchaseContracts
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderBy(x => x.CreatedAt);

    public IQueryable<ShipmentLoadPlannedSalesContract> QuerySales(Guid shipmentLoadKey) =>
        context.ShipmentLoadsPlannedSalesContracts
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderBy(x => x.CreatedAt);
}
