using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Cria as alocações NEGATIVAS da confirmação de uma nota de devolução, distribuindo a
/// quantidade devolvida proporcionalmente à distribuição VIGENTE do item de origem entre
/// (contrato, liberação) — respeita realocações já feitas: se o volume foi movido para o
/// contrato B, a devolução restaura o saldo do B, não o do contrato fiscal original.
/// As linhas negativas são gravadas no ITEM DA DEVOLUÇÃO (rastreáveis e removíveis no
/// cancelamento/estorno da devolução); o "saldo da família" de um item de origem é
/// Σ das linhas do próprio item + das linhas dos itens de devolução que apontam para ele.
/// Retorna as liberações afetadas para o chamador recalcular o ShippedQuantity.
/// </summary>
public class SalesContractsAllocationCreateForReturnService(IUnitOfWork db)
{
    public async Task<ISet<Guid>> ExecuteAsync(SalesInvoice returnInvoice, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var affectedReleases = new HashSet<Guid>();
        var affectedContracts = new Dictionary<Guid, SalesContract>();
        var pending = new List<SalesContractAllocation>();

        var items = returnInvoice.Items
            .Where(i => i.Key != null && i.SalesInvoiceItemOriginKey != null)
            .ToList();

        foreach (var item in items)
        {
            // Idempotência: item de devolução já lançado no ledger não gera linhas novas.
            var alreadyAllocated = await db.Context.SalesContractsAllocations
                .AnyAsync(a => a.SalesInvoiceItemKey == item.Key!.Value);
            if (alreadyAllocated)
                continue;

            var originKey = item.SalesInvoiceItemOriginKey!.Value;

            // Distribuição vigente da família do item de origem por (contrato, liberação):
            // linhas do próprio item (faturamento + pares de realocação) e linhas de
            // devoluções anteriores (itens que apontam para a mesma origem).
            var groups = await db.Context.SalesContractsAllocations
                .Where(a => a.SalesInvoiceItemKey == originKey ||
                            (a.SalesInvoiceItem!.SalesInvoiceItemOriginKey == originKey))
                .GroupBy(a => new { a.SalesContractKey, a.SalesShipmentReleaseKey })
                .Select(g => new
                {
                    g.Key.SalesContractKey,
                    g.Key.SalesShipmentReleaseKey,
                    Net = g.Sum(a => a.Volume),
                })
                .ToListAsync();

            var positiveGroups = groups.Where(g => g.Net > 0).ToList();

            if (positiveGroups.Count == 0)
            {
                // Origem sem saldo no ledger (não deve ocorrer pós-backfill): melhor
                // esforço no contrato fiscal do item, sem liberação.
                if (item.SalesContractKey is null)
                    continue;
                positiveGroups = [new { SalesContractKey = item.SalesContractKey.Value,
                    SalesShipmentReleaseKey = (Guid?)null, Net = item.Quantity }];
            }

            var totalNet = positiveGroups.Sum(g => g.Net);
            var toReturn = item.Quantity;

            // Rateio proporcional com resíduo de arredondamento na maior parcela.
            var shares = positiveGroups
                .Select(g => new
                {
                    Group = g,
                    Share = decimal.Round(toReturn * g.Net / totalNet, 3, MidpointRounding.ToEven),
                })
                .ToList();

            var residue = toReturn - shares.Sum(s => s.Share);
            var largest = shares.OrderByDescending(s => s.Group.Net).First();

            foreach (var share in shares)
            {
                var volume = share.Share + (ReferenceEquals(share, largest) ? residue : 0m);
                if (volume == 0m)
                    continue;

                var contract = await GetContractAsync(affectedContracts, share.Group.SalesContractKey);

                var allocation = new SalesContractAllocation
                {
                    SalesContractKey = contract.Key,
                    SalesInvoiceItemKey = item.Key!.Value,
                    SalesShipmentReleaseKey = share.Group.SalesShipmentReleaseKey,
                    Volume = -volume,
                    InvoiceUnitPrice = item.UnitPrice,
                    ContractPrice = contract.Price,
                    PriceDifference = decimal.Round(
                        -volume * (item.UnitPrice - contract.Price), 2, MidpointRounding.ToEven),
                    Origin = SalesContractAllocationOrigin.Return,
                    ApprovedAt = DateTime.Now,
                    ApprovedBy = userName,
                };

                pending.Add(allocation);
                await db.Context.SalesContractsAllocations.AddAsync(allocation);

                if (share.Group.SalesShipmentReleaseKey is { } releaseKey)
                    affectedReleases.Add(releaseKey);
            }
        }

        // Derivado-da-soma (banco + pendentes). Itens de devolução entram com fator 1:
        // a confirmação fecha o item com DeliveredQuantity = Quantity (sem quebra).
        foreach (var (contractKey, contract) in affectedContracts)
        {
            var persisted = await SalesContractsRecalculateBalanceService
                .CalculateAllocatedAsync(db.Context, contractKey);
            var pendingSum = pending
                .Where(a => a.SalesContractKey == contractKey)
                .Sum(a => a.Volume);

            contract.AllocatedVolume = decimal.Round(persisted + pendingSum, 3, MidpointRounding.ToEven);
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();

        return affectedReleases;
    }

    private async Task<SalesContract> GetContractAsync(
        Dictionary<Guid, SalesContract> cache, Guid contractKey)
    {
        if (cache.TryGetValue(contractKey, out var cached))
            return cached;

        var contract = await db.Context.SalesContracts
                           .FirstOrDefaultAsync(c => c.Key == contractKey)
                       ?? throw new ApplicationException("Contrato de venda não encontrado.");

        cache[contractKey] = contract;
        return contract;
    }
}
