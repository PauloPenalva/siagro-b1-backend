using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsGetShipmentReleasesAvailableService(
    IUnitOfWork db,
    ILogger<PurchaseContractsGetShipmentReleasesAvailableService> logger
    )
{
    public IQueryable<PurchaseContract> Query()
    {
        return db.Context.PurchaseContracts
            .Include(x => x.ShipmentReleases)
            .Where(p => p.Status == ContractStatus.Approved &&
                        (p.TotalVolume - p.ShipmentReleases
                             .Where(x =>  x.Status != ReleaseStatus.Cancelled)
                             .Sum(x => x.ReleasedQuantity)) > 0
                     );
    }
}