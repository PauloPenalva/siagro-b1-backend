using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public StorageTransactionsCreateService StorageCreate() =>
        new(Db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro Ltda" }),
            new FakeItemService(new() { [Item] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro" }),
            new ShipmentReleasesRecalculateShippedService(Db.Context),
            new ShipmentReleaseMovementGuardService(Db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    public WarehouseReconciliationsSendApprovalService SendApproval() => new(Db, Guard(), Resource);
    public WarehouseReconciliationsWithdrawApprovalService Withdraw() => new(Db, Resource);
    public WarehouseReconciliationsRejectService Reject() => new(Db, Resource);

    public WarehouseReconciliationsApprovalService Approval() =>
        new(Db, Guard(), StorageCreate(), Resource,
            NullLogger<WarehouseReconciliationsApprovalService>.Instance);

    public async Task<WarehouseReconciliation> CreateApprovedAsync(decimal reportedBalance, DateTime referenceDate)
    {
        var r = await CreateDraftAsync(reportedBalance, referenceDate);
        await SendApproval().ExecuteAsync(r.Key, "tester");
        await Approval().ExecuteAsync(r.Key, "ok", "approver");
        return r;
    }

    public Task<decimal> CurrentBalanceAsync() =>
        StorageTransactionsWarehouseBalanceService.CalculateAsync(Db.Context, ThirdPartyWarehouse, Item);
}
