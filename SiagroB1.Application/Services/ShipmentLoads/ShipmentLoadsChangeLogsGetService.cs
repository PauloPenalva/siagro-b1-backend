using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura do log de alterações de uma carga, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class ShipmentLoadsChangeLogsGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadChangeLog> QueryAll(Guid shipmentLoadKey) =>
        context.ShipmentLoadsChangeLogs
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.ChangedAt);
}
