using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Remove TODAS as alocações dos itens de uma invoice (inclui pares de realocação — o
/// volume "morre" com a nota) e recalcula, derivado-da-soma, o AllocatedVolume dos
/// contratos e o ShippedQuantity das liberações afetadas. Caminho cascade-safe usado
/// pelos fluxos de cancelamento e estorno de confirmação (<see cref="CommitMode.Deferred"/>
/// compõe na transação do chamador; as somas excluem as linhas removidas por chave, então
/// não dependem de flush intermediário).
/// </summary>
public class SalesContractsAllocationDeleteForInvoiceService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid salesInvoiceKey, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var allocations = await db.Context.SalesContractsAllocations
            .Where(a => a.SalesInvoiceItem!.SalesInvoiceKey == salesInvoiceKey)
            .ToListAsync();

        if (allocations.Count == 0)
            return;

        var deletedKeys = allocations.Select(a => a.Key).ToList();
        var affectedContracts = allocations.Select(a => a.SalesContractKey).Distinct().ToList();
        var affectedReleases = allocations
            .Where(a => a.SalesShipmentReleaseKey != null)
            .Select(a => a.SalesShipmentReleaseKey!.Value)
            .Distinct()
            .ToList();

        db.Context.SalesContractsAllocations.RemoveRange(allocations);

        // Derivado-da-soma excluindo as linhas removidas (ainda visíveis no banco até o flush).
        foreach (var contractKey in affectedContracts)
        {
            var contract = await db.Context.SalesContracts
                .FirstOrDefaultAsync(c => c.Key == contractKey);
            if (contract is null)
                continue;

            var remaining = await db.Context.SalesContractsAllocations
                .Where(a => a.SalesContractKey == contractKey && !deletedKeys.Contains(a.Key))
                .SumAsync(a => a.Volume *
                    (a.SalesInvoiceItem!.DeliveryStatus == Domain.Enums.SalesInvoiceDeliveryStatus.Closed
                     && a.SalesInvoiceItem.Quantity != 0
                        ? (a.SalesInvoiceItem.DeliveredQuantity - a.SalesInvoiceItem.QuantityLoss)
                          / a.SalesInvoiceItem.Quantity
                        : 1m));

            contract.AllocatedVolume = decimal.Round(remaining, 3, MidpointRounding.ToEven);
        }

        foreach (var releaseKey in affectedReleases)
        {
            var release = await db.Context.SalesShipmentReleases
                .FirstOrDefaultAsync(r => r.Key == releaseKey);
            if (release is null)
                continue;

            release.ShippedQuantity = await db.Context.SalesContractsAllocations
                .Where(a => a.SalesShipmentReleaseKey == releaseKey && !deletedKeys.Contains(a.Key))
                .SumAsync(a => a.Volume);
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }
}
