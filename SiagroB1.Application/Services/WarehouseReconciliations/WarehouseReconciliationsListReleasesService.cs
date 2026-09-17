using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>Distribuição gravada da perda, para as telas de detalhe e aprovação (GAC-1164 §9.9).</summary>
public class WarehouseReconciliationsListReleasesService(IUnitOfWork db)
{
    public async Task<List<WarehouseReconciliationReleaseLineDto>> ExecuteAsync(Guid reconciliationKey)
    {
        var lines = await db.Context.WarehouseReconciliationReleases
            .AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == reconciliationKey)
            .Select(x => new
            {
                x.ShipmentReleaseKey,
                x.ShipmentRelease!.ReleaseDate,
                ContractCode = x.ShipmentRelease.PurchaseContract!.Code,
                x.ShipmentRelease.PurchaseContract.CardCode,
                x.ShipmentRelease.PurchaseContract.CardName,
                x.Quantity,
                x.PurchaseStorageTransactionKey,
                x.LossStorageTransactionKey,
            })
            .ToListAsync();

        var transactionKeys = lines
            .SelectMany(x => new[] { x.PurchaseStorageTransactionKey, x.LossStorageTransactionKey })
            .Where(k => k.HasValue).Select(k => k!.Value).ToList();

        var codes = await db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => transactionKeys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Code);

        string? CodeOf(Guid? key) => key is Guid k && codes.TryGetValue(k, out var c) ? c : null;

        return lines
            .OrderBy(x => x.ReleaseDate).ThenBy(x => x.ContractCode)
            .Select(x => new WarehouseReconciliationReleaseLineDto
            {
                ShipmentReleaseKey = x.ShipmentReleaseKey,
                ReleaseDate = x.ReleaseDate,
                PurchaseContractCode = x.ContractCode,
                CardCode = x.CardCode,
                CardName = x.CardName,
                Quantity = x.Quantity,
                PurchaseTransactionCode = CodeOf(x.PurchaseStorageTransactionKey),
                LossTransactionCode = CodeOf(x.LossStorageTransactionKey),
            })
            .ToList();
    }
}
