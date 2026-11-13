using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// GAC-1214: deriva as alocações do contrato de compra a partir das liberações de devolução
/// (<see cref="ReleaseOrigin.SalesReturn"/>). A devolução devolve o saldo ao contrato (alocação
/// negativa no romaneio gerador, tipo 12) e cada saída/perda/devolução da liberação volta a
/// consumi-lo. O conjunto esperado é recalculado do zero a cada chamada e aplicado por diferença,
/// então o método é idempotente. Sempre consulta o banco e sempre salva; participa da transação
/// que estiver aberta no contexto.
/// </summary>
public static class PurchaseContractsReturnReleaseAllocationSync
{
    private static readonly StorageTransactionType[] ConsumerTypes =
    [
        StorageTransactionType.SalesShipment,
        StorageTransactionType.SalesShipmentReturn,
        StorageTransactionType.WarehouseLoss,
    ];

    public static async Task SyncAsync(AppDbContext context, Guid shipmentReleaseKey)
    {
        var release = await context.ShipmentReleases.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == shipmentReleaseKey && r.Origin == ReleaseOrigin.SalesReturn);
        if (release is null) return;

        await ApplyAsync(context, [release.PurchaseContractKey]);
    }

    public static async Task SyncByGeneratorAsync(AppDbContext context, Guid generatorStorageTransactionKey)
    {
        var contracts = await context.ShipmentReleases.AsNoTracking()
            .Where(r => r.Origin == ReleaseOrigin.SalesReturn
                        && r.GeneratedByStorageTransactionKey == generatorStorageTransactionKey)
            .Select(r => r.PurchaseContractKey)
            .Distinct()
            .ToListAsync();

        await ApplyAsync(context, contracts);
    }

    public static Task SyncContractAsync(AppDbContext context, Guid purchaseContractKey) =>
        ApplyAsync(context, [purchaseContractKey]);

    public static async Task<bool> IsDerivedAsync(AppDbContext context, Guid storageTransactionKey)
    {
        if (await context.ShipmentReleases.AsNoTracking().AnyAsync(r =>
                r.Origin == ReleaseOrigin.SalesReturn && r.GeneratedByStorageTransactionKey == storageTransactionKey))
            return true;

        var releaseKey = await context.StorageTransactions.AsNoTracking()
            .Where(t => t.Key == storageTransactionKey && ConsumerTypes.Contains(t.TransactionType))
            .Select(t => t.ShipmentReleaseKey)
            .FirstOrDefaultAsync();
        if (releaseKey is null) return false;

        return await context.ShipmentReleases.AsNoTracking()
            .AnyAsync(r => r.Key == releaseKey && r.Origin == ReleaseOrigin.SalesReturn);
    }

    /// <summary>
    /// Sincroniza TODAS as liberações de devolução dos contratos informados (e não só a pedida):
    /// o volume do gerador é a soma das suas liberações no contrato, então olhar uma isolada
    /// apagaria a parte das outras.
    /// </summary>
    private static async Task ApplyAsync(AppDbContext context, IReadOnlyCollection<Guid> contractKeys)
    {
        if (contractKeys.Count == 0) return;

        var releases = await context.ShipmentReleases.AsNoTracking()
            .Where(r => r.Origin == ReleaseOrigin.SalesReturn && contractKeys.Contains(r.PurchaseContractKey))
            .ToListAsync();

        var releaseKeys = releases.Select(r => r.Key).ToList();
        var generatorKeys = releases.Where(r => r.GeneratedByStorageTransactionKey != null)
            .Select(r => r.GeneratedByStorageTransactionKey!.Value).Distinct().ToList();

        var movements = await context.StorageTransactions.AsNoTracking()
            .Where(t => t.ShipmentReleaseKey != null
                        && releaseKeys.Contains(t.ShipmentReleaseKey.Value)
                        && ConsumerTypes.Contains(t.TransactionType))
            .Select(t => new { t.Key, t.ShipmentReleaseKey, t.TransactionType, t.TransactionStatus, t.NetWeight })
            .ToListAsync();

        var liveGenerators = (await context.StorageTransactions.AsNoTracking()
                .Where(t => generatorKeys.Contains(t.Key) && t.TransactionStatus != StorageTransactionsStatus.Cancelled)
                .Select(t => t.Key)
                .ToListAsync())
            .ToHashSet();

        var expected = new Dictionary<(Guid Contract, Guid Transaction), decimal>();
        void Add(Guid contract, Guid transaction, decimal volume)
        {
            expected.TryGetValue((contract, transaction), out var current);
            expected[(contract, transaction)] = current + volume;
        }

        var candidates = new HashSet<(Guid Contract, Guid Transaction)>();

        foreach (var release in releases)
        {
            var own = movements.Where(m => m.ShipmentReleaseKey == release.Key).ToList();
            decimal shipped = 0m;

            foreach (var m in own)
            {
                candidates.Add((release.PurchaseContractKey, m.Key));
                if (m.TransactionStatus == StorageTransactionsStatus.Cancelled) continue;

                // Mesmo sinal do consumo da liberação: 7 e 13 consomem o contrato, 12 devolve.
                var signed = ShipmentReleasesRecalculateShippedService.ShippedSign(
                    ReleaseOrigin.SalesReturn, m.TransactionType) * m.NetWeight;
                shipped += signed;
                if (signed != 0m) Add(release.PurchaseContractKey, m.Key, signed);
            }

            if (release.GeneratedByStorageTransactionKey is not { } generator) continue;
            candidates.Add((release.PurchaseContractKey, generator));
            if (!liveGenerators.Contains(generator)) continue;

            var live = release.Status is not (ReleaseStatus.Cancelled or ReleaseStatus.Completed);
            var returned = shipped + (live ? release.ReleasedQuantity - shipped : 0m);
            if (returned != 0m) Add(release.PurchaseContractKey, generator, -returned);
        }

        var candidateKeys = candidates.Select(c => c.Transaction).Distinct().ToList();
        var existing = await context.PurchaseContractsAllocations
            .Where(a => contractKeys.Contains(a.PurchaseContractKey) && candidateKeys.Contains(a.StorageTransactionKey))
            .ToListAsync();

        var existingIds = new HashSet<(Guid, Guid)>();
        foreach (var allocation in existing)
        {
            var id = (allocation.PurchaseContractKey, allocation.StorageTransactionKey);
            if (!candidates.Contains(id)) continue;
            existingIds.Add(id);

            if (!expected.TryGetValue(id, out var volume) || volume == 0m)
                context.PurchaseContractsAllocations.Remove(allocation);
            else if (allocation.Volume != volume)
                allocation.Volume = volume;
        }

        foreach (var ((contract, transaction), volume) in expected)
        {
            if (volume == 0m || existingIds.Contains((contract, transaction))) continue;
            context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
            {
                PurchaseContractKey = contract,
                StorageTransactionKey = transaction,
                Volume = volume,
                ApprovedAt = DateTime.Now,
                ApprovedBy = "system",
            });
        }

        await context.SaveChangesAsync();

        foreach (var contractKey in contractKeys)
        {
            var contract = await context.PurchaseContracts.FirstOrDefaultAsync(c => c.Key == contractKey);
            if (contract is null) continue;

            contract.AllocatedVolume = await context.PurchaseContractsAllocations
                .Where(a => a.PurchaseContractKey == contractKey)
                .SumAsync(a => a.Volume);
        }

        await context.SaveChangesAsync();
    }
}
