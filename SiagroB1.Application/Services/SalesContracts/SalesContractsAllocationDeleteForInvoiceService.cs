using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesShipmentReleases;
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

        // Todas as linhas destes itens estão sendo removidas, então excluir os ITENS da soma
        // já exclui exatamente as linhas removidas — que continuam visíveis no banco até o
        // flush. É também o eixo que as fórmulas canônicas de saldo aceitam.
        var deletedItemKeys = allocations.Select(a => a.SalesInvoiceItemKey).Distinct().ToList();
        var affectedContracts = allocations.Select(a => a.SalesContractKey).Distinct().ToList();
        var affectedReleases = allocations
            .Where(a => a.SalesShipmentReleaseKey != null)
            .Select(a => a.SalesShipmentReleaseKey!.Value)
            .Distinct()
            .ToList();

        db.Context.SalesContractsAllocations.RemoveRange(allocations);

        // Derivado-da-soma pelas fórmulas canônicas dos dois eixos, que já aplicam a regra da
        // entrega (item em aberto consome o nominal; encerrado, o líquido) concentrando a
        // quebra na linha dona.
        foreach (var contractKey in affectedContracts)
        {
            var contract = await db.Context.SalesContracts
                .FirstOrDefaultAsync(c => c.Key == contractKey);
            if (contract is null)
                continue;

            contract.AllocatedVolume = await SalesContractsRecalculateBalanceService
                .CalculateAllocatedAsync(db.Context, contractKey, deletedItemKeys);
        }

        foreach (var releaseKey in affectedReleases)
        {
            var release = await db.Context.SalesShipmentReleases
                .FirstOrDefaultAsync(r => r.Key == releaseKey);
            if (release is null)
                continue;

            release.ShippedQuantity = await SalesShipmentReleasesRecalculateShippedService
                .CalculateShippedAsync(db.Context, releaseKey, deletedItemKeys);
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }
}
