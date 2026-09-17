using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public WarehouseReconciliationsGuardService Guard() =>
        new(Db, new WarehouseComplementService(Db), ReleaseBalance(), Resource);

    public WarehouseReconciliationsDescriptionService Descriptions() =>
        new(new FakeItemService(new() { [Item] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro" }),
            new FakeBusinessPartnerService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro Ltda" }));

    public WarehouseReconciliationsCreateService Create() =>
        new(Db, new FakeDocNumberSequenceService(), Descriptions(), Guard(), Resource);

    public WarehouseReconciliationsUpdateService Update() =>
        new(Db, Descriptions(), Guard(), Resource);

    public WarehouseReconciliationsGetBalancePreviewService Preview() =>
        new(Db, new WarehouseComplementService(Db), ReleaseBalance(), Guard());

    public async Task<WarehouseReconciliation> CreateDraftAsync(decimal reportedBalance, DateTime? referenceDate = null)
    {
        var reason = await SeedReasonAsync();
        var reconciliation = NewReconciliation(reason.Key, reportedBalance, referenceDate);
        await Create().ExecuteAsync(reconciliation, "tester");
        return reconciliation;
    }

    /// <summary>Grava direto no banco, sem passar pelos serviços — para montar cenário de guard.</summary>
    public async Task<WarehouseReconciliation> SeedWithStatusAsync(
        WarehouseReconciliationStatus status, DateTime referenceDate, decimal difference = -10m)
    {
        var reason = await SeedReasonAsync();
        var reconciliation = NewReconciliation(reason.Key, 100m, referenceDate);
        reconciliation.Key = Guid.NewGuid();
        reconciliation.Status = status;
        reconciliation.Difference = difference;
        reconciliation.ApprovedAt = status == WarehouseReconciliationStatus.Approved ? DateTime.Now : null;
        Db.Context.WarehouseReconciliations.Add(reconciliation);
        await Db.Context.SaveChangesAsync();
        return reconciliation;
    }

    public Task<WarehouseReconciliation> ReloadAsync(Guid key) =>
        Db.Context.WarehouseReconciliations.AsNoTracking().SingleAsync(x => x.Key == key);
}
