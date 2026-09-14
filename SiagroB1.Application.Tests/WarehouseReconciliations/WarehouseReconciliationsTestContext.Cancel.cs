using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public WarehouseReconciliationsCancelService Cancel() =>
        new(Db,
            new StorageTransactionsCancelService(Db, new ShipmentReleasesRecalculateShippedService(Db.Context)),
            Resource,
            NullLogger<WarehouseReconciliationsCancelService>.Instance);
}
