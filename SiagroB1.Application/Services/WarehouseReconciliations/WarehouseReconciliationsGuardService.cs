using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Regras da Conferência de Saldo de Armazém compartilhadas por criar, editar, enviar e aprovar.
/// Todas lançam <see cref="ApplicationException"/> com mensagem de negócio e devem ser chamadas
/// ANTES de abrir transação (o catch dos serviços embrulharia a mensagem).
/// </summary>
public class WarehouseReconciliationsGuardService(
    IUnitOfWork db,
    IWarehouseComplementService complements,
    IStringLocalizer<Resource> resource)
{
    public async Task EnsureCanPersistAsync(WarehouseReconciliation r)
    {
        if (string.IsNullOrWhiteSpace(r.BranchCode) ||
            string.IsNullOrWhiteSpace(r.WarehouseCode) ||
            string.IsNullOrWhiteSpace(r.ItemCode) ||
            string.IsNullOrWhiteSpace(r.UnitOfMeasureCode) ||
            r.ReasonKey == Guid.Empty)
            throw Fail("WAREHOUSE_RECONCILIATION_REQUIRED_FIELDS");

        // Sem linha de complemento = terceiros. Armazém próprio tem lotes e quebra técnica por lote.
        if ((await complements.GetAsync(r.WarehouseCode))?.IsOwn == true)
            throw Fail("WAREHOUSE_RECONCILIATION_OWN_WAREHOUSE");

        if (r.ReportedBalance < decimal.Zero)
            throw Fail("WAREHOUSE_RECONCILIATION_NEGATIVE_REPORTED_BALANCE");

        if (r.ReferenceDate.Date > DateTime.Today)
            throw Fail("WAREHOUSE_RECONCILIATION_FUTURE_DATE");

        var reason = await db.Context.WarehouseReconciliationReasons
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == r.ReasonKey);

        if (reason is not { Active: true })
            throw Fail("WAREHOUSE_RECONCILIATION_REASON_INACTIVE");

        // Conferência anterior à última aprovada apuraria diferença sobre um saldo que já recebeu
        // o ajuste posterior — ajustaria duas vezes.
        var lastApproved = await LastApprovedReferenceDateAsync(r.WarehouseCode, r.ItemCode, r.Key);
        if (lastApproved.HasValue && r.ReferenceDate.Date < lastApproved.Value.Date)
            throw Fail("WAREHOUSE_RECONCILIATION_DATE_BEFORE_LAST_APPROVED");

        if (await HasOpenAsync(r.WarehouseCode, r.ItemCode, r.Key))
            throw Fail("WAREHOUSE_RECONCILIATION_ALREADY_OPEN");
    }

    /// <summary>Recalcula o saldo do sistema até a data de referência e a diferença.</summary>
    public async Task RefreshSnapshotAsync(WarehouseReconciliation r)
    {
        r.SystemBalance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            db.Context, r.WarehouseCode, r.ItemCode, r.ReferenceDate);
        r.Difference = r.ReportedBalance - r.SystemBalance;
    }

    public Task<DateTime?> LastApprovedReferenceDateAsync(
        string warehouseCode, string itemCode, Guid? ignoreKey = null) =>
        db.Context.WarehouseReconciliations
            .AsNoTracking()
            .Where(x => x.WarehouseCode == warehouseCode &&
                        x.ItemCode == itemCode &&
                        x.Status == WarehouseReconciliationStatus.Approved &&
                        x.Key != ignoreKey)
            .MaxAsync(x => (DateTime?)x.ReferenceDate);

    public Task<bool> HasOpenAsync(string warehouseCode, string itemCode, Guid? ignoreKey = null) =>
        db.Context.WarehouseReconciliations
            .AsNoTracking()
            .AnyAsync(x => x.WarehouseCode == warehouseCode &&
                           x.ItemCode == itemCode &&
                           x.Key != ignoreKey &&
                           (x.Status == WarehouseReconciliationStatus.Draft ||
                            x.Status == WarehouseReconciliationStatus.InApproval));

    private ApplicationException Fail(string key) => new(resource[key].Value);
}
