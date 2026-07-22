using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Estorna uma realocação: remove o grupo inteiro de linhas −/+ (resolvido pelo
/// ReallocationGroupKey de qualquer linha do par) e recalcula contratos e liberações
/// derivado-da-soma. O estorno devolve o consumo à liberação de origem — por isso exige
/// que ela tenha status válido e saldo para reabsorver o volume.
/// </summary>
public class SalesContractsReallocationDeleteService(
    IUnitOfWork db,
    ILogger<SalesContractsReallocationDeleteService> logger)
{
    public async Task ExecuteWithTransactionAsync(Guid key, string userName)
    {
        try
        {
            await db.BeginTransactionAsync();
            await ExecuteAsync(key, userName);
            await db.CommitAsync();
        }
        catch (NotFoundException)
        {
            await db.RollbackAsync();
            throw;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw new DefaultException(e.Message);
        }
    }

    private async Task ExecuteAsync(Guid key, string userName)
    {
        var allocation = await db.Context.SalesContractsAllocations
                             .FirstOrDefaultAsync(a => a.Key == key)
                         ?? throw new NotFoundException("Alocação não encontrada.");

        if (allocation.Origin != SalesContractAllocationOrigin.Reallocation
            || allocation.ReallocationGroupKey is null)
            throw new ApplicationException("Somente realocações podem ser estornadas.");

        var groupKey = allocation.ReallocationGroupKey.Value;
        var group = await db.Context.SalesContractsAllocations
            .Where(a => a.ReallocationGroupKey == groupKey)
            .ToListAsync();

        var deletedKeys = group.Select(a => a.Key).ToList();
        var itemKey = allocation.SalesInvoiceItemKey;

        var invoiceStatus = await db.Context.SalesInvoicesItems
            .Where(i => i.Key == itemKey)
            .Select(i => i.SalesInvoice!.InvoiceStatus)
            .FirstOrDefaultAsync();

        if (invoiceStatus == InvoiceStatus.Cancelled)
            throw new ApplicationException("Nota cancelada: não é possível estornar a realocação.");

        var contractKeys = group.Select(a => a.SalesContractKey).Distinct().ToList();
        var contracts = await db.Context.SalesContracts
            .Where(c => contractKeys.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key);

        if (contracts.Values.Any(c => c.Status == ContractStatus.Finished))
            throw new ApplicationException("Contrato encerrado: não é possível estornar a realocação.");

        // Realocações encadeadas: após remover o grupo, nenhum saldo da família do item
        // pode ficar negativo em nenhum (contrato, liberação) — senão o estorno retiraria
        // volume que outra realocação já consumiu adiante.
        var residualGroups = await db.Context.SalesContractsAllocations
            .Where(a => !deletedKeys.Contains(a.Key) &&
                        (a.SalesInvoiceItemKey == itemKey ||
                         a.SalesInvoiceItem!.SalesInvoiceItemOriginKey == itemKey))
            .GroupBy(a => new { a.SalesContractKey, a.SalesShipmentReleaseKey })
            .Select(g => g.Sum(a => a.Volume))
            .ToListAsync();

        if (residualGroups.Any(net => net < 0))
            throw new ApplicationException(
                "Não é possível estornar: existem realocações posteriores dependentes deste volume.");

        // Liberações: delta por liberação = −Σ das linhas removidas. Delta positivo significa
        // que a liberação reabsorve consumo (a de origem) — precisa de status válido e saldo.
        var releaseDeltas = group
            .Where(a => a.SalesShipmentReleaseKey != null)
            .GroupBy(a => a.SalesShipmentReleaseKey!.Value)
            .ToDictionary(g => g.Key, g => -g.Sum(a => a.Volume));

        var releases = await db.Context.SalesShipmentReleases
            .Where(r => releaseDeltas.Keys.Contains(r.Key))
            .ToDictionaryAsync(r => r.Key);

        foreach (var (releaseKey, delta) in releaseDeltas)
        {
            if (delta <= 0 || !releases.TryGetValue(releaseKey, out var release))
                continue;

            if (release.Status is ReleaseStatus.Completed or ReleaseStatus.Cancelled or ReleaseStatus.Paused)
                throw new ApplicationException(
                    "Liberação de origem finalizada/cancelada/pausada: não é possível estornar a realocação.");

            if (delta > release.AvailableQuantity)
                throw new ApplicationException(
                    $"Saldo da liberação de origem insuficiente para reabsorver o volume " +
                    $"({release.AvailableQuantity:N3}).");
        }

        db.Context.SalesContractsAllocations.RemoveRange(group);

        // Recalcs derivado-da-soma excluindo as linhas removidas.
        foreach (var contract in contracts.Values)
        {
            var remaining = await db.Context.SalesContractsAllocations
                .Where(a => a.SalesContractKey == contract.Key && !deletedKeys.Contains(a.Key))
                .SumAsync(a => a.Volume *
                    (a.SalesInvoiceItem!.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed
                     && a.SalesInvoiceItem.Quantity != 0
                        ? (a.SalesInvoiceItem.DeliveredQuantity - a.SalesInvoiceItem.QuantityLoss)
                          / a.SalesInvoiceItem.Quantity
                        : 1m));

            contract.AllocatedVolume = decimal.Round(remaining, 3, MidpointRounding.ToEven);
        }

        foreach (var release in releases.Values)
        {
            release.ShippedQuantity = await db.Context.SalesContractsAllocations
                .Where(a => a.SalesShipmentReleaseKey == release.Key && !deletedKeys.Contains(a.Key))
                .SumAsync(a => a.Volume);
        }

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Realocação {GroupKey} estornada por {UserName} ({Lines} linhas).",
            groupKey, userName, group.Count);
    }
}
