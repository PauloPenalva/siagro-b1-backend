using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public WarehouseReconciliationsDistributeLossService DistributeLoss() => new(Db, Resource);

    public Task DistributeAsync(Guid key, params (ShipmentRelease Release, decimal Quantity)[] lines) =>
        DistributeLoss().ExecuteAsync(
            key,
            lines.Select(l => new WarehouseReconciliationLossLine(l.Release.Key, l.Quantity)).ToList(),
            "tester");
}
