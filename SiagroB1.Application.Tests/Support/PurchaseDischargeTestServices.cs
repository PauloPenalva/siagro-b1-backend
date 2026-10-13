using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Acerto de descarga da troca nota com dependências reais sobre o banco InMemory.</summary>
public static class PurchaseDischargeTestServices
{
    public static PurchaseDischargeTransactionsSyncService Sync(UnitOfWork db) =>
        new(db,
            new StorageTransactionsCreateService(db, new FakeDocNumberSequenceService(),
                new FakeBusinessPartnerService(new()), new FakeItemService(new()), new FakeWarehouseService(new()),
                new ShipmentReleasesRecalculateShippedService(db.Context),
                new ShipmentReleaseMovementGuardService(db.Context),
                NullLogger<StorageTransactionsCreateService>.Instance),
            new PurchaseContractsAllocationCreateService(db,
                new StorageTransactionsGetService(db, NullLogger<StorageTransactionsGetService>.Instance)),
            new PurchaseContractsAllocationDeleteService(db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance));
}
