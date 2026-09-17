using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Saldo a embarcar das liberações de um armazém+produto numa data (GAC-1164 §9). É o "saldo do
/// sistema" da Conferência de Saldo de Armazém.
/// </summary>
/// <remarks>
/// <b>Por que não o saldo por romaneios</b> (<c>StorageTransactionsWarehouseBalanceService</c>): na
/// liberação Standard a Expedição só grava a Compra(8) AO EMBARCAR, em par com a Saída(7). Enquanto o
/// grão está parado no armazém de terceiros aquele saldo fica em zero — na homologação a conferência
/// apurou 0 com 196.430 kg a embarcar.
/// <para>
/// <b>Na data:</b> parte do saldo de hoje (Released − Shipped), soma de volta o que foi romaneado
/// contra a liberação DEPOIS do fim do dia de referência (mesmos tipos e sinais do recálculo, via
/// <see cref="ShipmentReleasesRecalculateShippedService.ShippedSign"/>) e descarta liberação emitida
/// depois da data. Romaneio sem data conta como anterior, igual ao saldo por romaneios.
/// </para>
/// <para>
/// <b><c>Completed</c>:</b> <c>ShipmentReleasesCloseService</c> finaliza a liberação sem zerar o
/// que sobrou de Released − Shipped — aquele saldo já foi dado por encerrado. Por isso ela
/// contribui SÓ com o que foi romaneado depois da data (<c>balanceAtDate = consumedAfter</c>), e
/// <c>CurrentBalance</c> sai zerado; nunca recebe perda (<c>CanReceiveLoss</c> já é falso pelo
/// status).
/// </para>
/// <para>
/// <b><c>CanReceiveLoss</c>:</b> além de <c>Actived</c> e saldo hoje &gt; 0, a liberação
/// <c>Standard</c> (com perna de compra) de um contrato <c>Finished</c> fica de fora — o contrato
/// já foi dado por entregue e não pode receber uma nova alocação. Origens sem perna de compra
/// (<see cref="ReleaseOriginRules.ShipsWithoutPurchaseLeg"/>) não alocam contrato, então essa
/// restrição não as afeta.
/// </para>
/// </remarks>
public class WarehouseReconciliationReleaseBalanceService(IUnitOfWork db)
{
    public async Task<List<WarehouseReconciliationReleaseBalanceDto>> ListAsync(
        string warehouseCode, string itemCode, DateTime referenceDate)
    {
        var limit = referenceDate.Date.AddDays(1);

        var releases = await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(r => r.DeliveryLocationCode == warehouseCode &&
                        r.PurchaseContract!.ItemCode == itemCode &&
                        (r.Status == ReleaseStatus.Actived ||
                         r.Status == ReleaseStatus.Paused ||
                         r.Status == ReleaseStatus.Completed) &&
                        r.ReleaseDate < limit)
            .Select(r => new
            {
                r.Key,
                r.ReleaseDate,
                r.Status,
                r.Origin,
                r.ReleasedQuantity,
                r.ShippedQuantity,
                r.PurchaseContractKey,
                ContractCode = r.PurchaseContract!.Code,
                r.PurchaseContract.CardCode,
                r.PurchaseContract.CardName,
                ContractStatus = r.PurchaseContract.Status,
            })
            .ToListAsync();

        if (releases.Count == 0)
            return [];

        var keys = releases.Select(r => r.Key).ToList();

        var movedAfter = await db.Context.StorageTransactions
            .AsNoTracking()
            .Where(t => t.ShipmentReleaseKey != null &&
                        keys.Contains(t.ShipmentReleaseKey.Value) &&
                        t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        t.TransactionDate != null &&
                        t.TransactionDate >= limit)
            .Select(t => new { ReleaseKey = t.ShipmentReleaseKey!.Value, t.TransactionType, t.NetWeight })
            .ToListAsync();

        return releases
            .Select(r =>
            {
                var consumedAfter = movedAfter
                    .Where(t => t.ReleaseKey == r.Key)
                    .Sum(t => ShipmentReleasesRecalculateShippedService.ShippedSign(r.Origin, t.TransactionType) * t.NetWeight);

                var current = r.ReleasedQuantity - r.ShippedQuantity;
                var isCompleted = r.Status == ReleaseStatus.Completed;

                // Completed é finalizada por ShipmentReleasesCloseService, que só troca o Status —
                // não zera o Released/Shipped que sobrou. Aquele saldo já foi dado por encerrado e
                // não pode inflar o saldo do sistema (fix round 2): só o que foi romaneado contra
                // ela DEPOIS da data ainda conta.
                var balanceAtDate = (isCompleted ? decimal.Zero : current) + consumedAfter;

                return new WarehouseReconciliationReleaseBalanceDto
                {
                    ShipmentReleaseKey = r.Key,
                    ReleaseDate = r.ReleaseDate,
                    Status = r.Status.ToString(),
                    Origin = r.Origin.ToString(),
                    PurchaseContractKey = r.PurchaseContractKey,
                    PurchaseContractCode = r.ContractCode,
                    CardCode = r.CardCode,
                    CardName = r.CardName,
                    BalanceAtReferenceDate = decimal.Round(balanceAtDate, 3, MidpointRounding.ToEven),
                    CurrentBalance = isCompleted ? decimal.Zero : decimal.Round(current, 3, MidpointRounding.ToEven),
                    CanReceiveLoss = r.Status == ReleaseStatus.Actived && current > decimal.Zero &&
                        (ReleaseOriginRules.ShipsWithoutPurchaseLeg(r.Origin) || r.ContractStatus != ContractStatus.Finished),
                };
            })
            .Where(x => x.BalanceAtReferenceDate > decimal.Zero)
            .OrderBy(x => x.ReleaseDate)
            .ThenBy(x => x.PurchaseContractCode)
            .ToList();
    }
}
