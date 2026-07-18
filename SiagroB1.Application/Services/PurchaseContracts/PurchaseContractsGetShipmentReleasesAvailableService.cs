using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsGetShipmentReleasesAvailableService(
    IUnitOfWork db,
    ILogger<PurchaseContractsGetShipmentReleasesAvailableService> logger
    )
{
    /// <summary>
    /// Espelho em SQL da regra de <see cref="PurchaseContract.TotalAvailableToRelease"/>
    /// (EF não traduz a propriedade [NotMapped] <c>ShipmentRelease.ConsumedQuantity</c>).
    /// Mantenha as duas em sincronia: liberação cancelada consome apenas o romaneado.
    /// </summary>
    public IQueryable<PurchaseContract> Query()
    {
        return db.Context.PurchaseContracts
            .Include(x => x.ShipmentReleases)
            .Where(p => p.Status == ContractStatus.Approved &&
                        (p.TotalVolume - p.ShipmentReleases
                             .Sum(x => x.Status == ReleaseStatus.Cancelled
                                 ? (x.ShippedQuantity > 0 ? x.ShippedQuantity : 0)
                                 : x.ReleasedQuantity)) > 0
                     );
    }
}