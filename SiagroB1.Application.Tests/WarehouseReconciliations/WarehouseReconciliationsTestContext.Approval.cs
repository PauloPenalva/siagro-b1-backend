using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public StorageTransactionsCreateService StorageCreate() =>
        new(Db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro Ltda", [Producer] = "Produtor Teste" }),
            new FakeItemService(new() { [Item] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro" }),
            new ShipmentReleasesRecalculateShippedService(Db.Context),
            new ShipmentReleaseMovementGuardService(Db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    public WarehouseReconciliationsSendApprovalService SendApproval() => new(Db, Guard(), Resource);
    public WarehouseReconciliationsWithdrawApprovalService Withdraw() => new(Db, Resource);
    public WarehouseReconciliationsRejectService Reject() => new(Db, Resource);

    public WarehouseReconciliationsApprovalService Approval()
    {
        var recalc = new ShipmentReleasesRecalculateShippedService(Db.Context);
        var guard = new ShipmentReleaseMovementGuardService(Db.Context);
        var create = StorageCreate();

        return new(
            Db,
            Guard(),
            create,
            new StorageTransactionsConfirmedService(Db, new FakeStringLocalizer<Resource>(), recalc, guard,
                NullLogger<StorageTransactionsConfirmedService>.Instance),
            new StorageTransactionsCopyService(Db, new FakeDocNumberSequenceService(), create, new FakeStringLocalizer<Resource>()),
            new PurchaseContractsAllocationCreateService(Db,
                new StorageTransactionsGetService(Db, NullLogger<StorageTransactionsGetService>.Instance)),
            recalc,
            Resource,
            NullLogger<WarehouseReconciliationsApprovalService>.Instance);
    }

    public async Task<WarehouseReconciliation> CreateApprovedAsync(
        decimal reportedBalance, DateTime referenceDate, params (ShipmentRelease Release, decimal Quantity)[] lines)
    {
        var r = await CreateDraftAsync(reportedBalance, referenceDate);
        await DistributeAsync(r.Key, lines);
        await SendApproval().ExecuteAsync(r.Key, "tester");
        await Approval().ExecuteAsync(r.Key, "ok", "approver");
        return r;
    }

    public Task<decimal> CurrentBalanceAsync() =>
        StorageTransactionsWarehouseBalanceService.CalculateAsync(Db.Context, ThirdPartyWarehouse, Item);
}
