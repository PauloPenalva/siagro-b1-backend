using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura dos comentários de uma carga, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class ShipmentLoadsCommentsGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadComment> QueryAll(Guid shipmentLoadKey) =>
        context.ShipmentLoadsComments
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.CommentedAt);
}
