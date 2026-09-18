using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura dos tickets de descarga de uma carga, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top/$expand.
/// </summary>
public class ShipmentLoadDischargesGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadDischarge> QueryAll(Guid shipmentLoadKey) =>
        context.ShipmentLoadsDischarges
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.DischargeDate)
            .ThenByDescending(x => x.CreatedAt);
}
