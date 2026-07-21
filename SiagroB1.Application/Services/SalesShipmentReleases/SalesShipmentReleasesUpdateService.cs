using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesUpdateService(AppDbContext context, ILogger<SalesShipmentReleasesUpdateService> logger)
{
    /// <summary>
    /// Se faz necessário deletar a liberação pendente e criar uma nova.
    /// </summary>
    public Task<SalesShipmentRelease?> ExecuteAsync(Guid key, SalesShipmentRelease entity)
    {
        throw new MethodAccessException("Update Sales Shipment Release unavailable.");
    }
}
