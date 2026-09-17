using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>Prévia para o formulário: saldo do sistema até a data e as travas que se aplicariam.</summary>
public class WarehouseReconciliationsGetBalancePreviewService(
    IUnitOfWork db,
    IWarehouseComplementService complements,
    WarehouseReconciliationReleaseBalanceService releaseBalances,
    WarehouseReconciliationsGuardService guard)
{
    public async Task<WarehouseReconciliationBalancePreviewDto> ExecuteAsync(
        string warehouseCode, string itemCode, DateTime referenceDate)
    {
        var releases = await releaseBalances.ListAsync(warehouseCode, itemCode, referenceDate);

        return new WarehouseReconciliationBalancePreviewDto
        {
            SystemBalance = releases.Sum(x => x.BalanceAtReferenceDate),
            Releases = releases,
            IsOwnWarehouse = (await complements.GetAsync(warehouseCode))?.IsOwn == true,
            LastApprovedReferenceDate = await guard.LastApprovedReferenceDateAsync(warehouseCode, itemCode),
            HasOpenReconciliation = await guard.HasOpenAsync(warehouseCode, itemCode),
        };
    }
}
