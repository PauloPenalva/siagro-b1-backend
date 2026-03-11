using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesUpdateService(AppDbContext context, ILogger<ShipmentReleasesUpdateService> logger)
{
    /// <summary>
    /// Se faz necessário deletar a liberação pendente e criar uma nova
    /// </summary>
    /// <param name="key"></param>
    /// <param name="entity"></param>
    /// <returns></returns>
    /// <exception cref="MethodAccessException"></exception>
    public Task<ShipmentRelease?> ExecuteAsync(Guid key, ShipmentRelease entity)
    {
        throw new MethodAccessException("Update Shipment Release unavailable.");
    }
}