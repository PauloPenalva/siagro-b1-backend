using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Estorna uma realocação ou uma conciliação: remove o grupo inteiro de linhas −/+ (resolvido pelo
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

        // Conciliação é um par −/+ com a mesma estrutura da realocação (mesmo
        // ReallocationGroupKey), só que sem liberação no destino — estorna pelo mesmo
        // caminho. As linhas com liberação nula simplesmente não entram em releaseDeltas.
        if (allocation.Origin is not (SalesContractAllocationOrigin.Reallocation
                                      or SalesContractAllocationOrigin.Reconciliation)
            || allocation.ReallocationGroupKey is null)
            throw new ApplicationException("Somente realocações e conciliações podem ser estornadas.");

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

        // Titularidade da diferença sobre o que SOBROU do item. Se a linha dona era uma das
        // removidas, a regra reelege sozinha (tipicamente a do faturamento) — é o que dispensa
        // o estorno de qualquer bookkeeping de titularidade.
        var remainingItemLines = await db.Context.SalesContractsAllocations
            .Include(a => a.SalesInvoiceItem)
            .Where(a => a.SalesInvoiceItemKey == itemKey && !deletedKeys.Contains(a.Key))
            .ToListAsync();

        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(remainingItemLines);

        // O grupo inteiro é sempre do mesmo item, então excluir o item da soma SQL já exclui
        // as linhas removidas — que ainda estão no banco, porque o SaveChanges vem depois.
        var item = await db.Context.SalesInvoicesItems.FirstAsync(i => i.Key == itemKey);

        await SalesContractsRecalculateBalanceService.RecalculateForTouchedItemAsync(
            db.Context, itemKey, item, remainingItemLines,
            extraContractKeys: contracts.Keys);

        // Mesma regra do contrato — o estorno devolve o LÍQUIDO à liberação de origem quando
        // a entrega já foi conferida. As liberações do grupo entram por extraReleaseKeys: a
        // do destino pode ter perdido a única linha do item e não apareceria em
        // remainingItemLines.
        await SalesShipmentReleasesRecalculateShippedService.RecalculateForTouchedItemAsync(
            db.Context, itemKey, item, remainingItemLines, extraReleaseKeys: releases.Keys);

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Realocação {GroupKey} estornada por {UserName} ({Lines} linhas).",
            groupKey, userName, group.Count);
    }
}
