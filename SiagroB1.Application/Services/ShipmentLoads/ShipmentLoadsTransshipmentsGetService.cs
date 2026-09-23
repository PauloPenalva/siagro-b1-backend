using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura dos transbordos de uma carga (GAC-1181), mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top/$expand.
/// </summary>
public class ShipmentLoadsTransshipmentsGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadTransshipment> QueryAll(Guid shipmentLoadKey) =>
        context.ShipmentLoadsTransshipments
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.Sequence);
}
