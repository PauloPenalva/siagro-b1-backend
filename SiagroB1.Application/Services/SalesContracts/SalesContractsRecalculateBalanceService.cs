using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsRecalculateBalanceService(AppDbContext context)
{
    /// <summary>
    /// Σ Volume assinado do ledger × fator efetivo do item, derivado-da-soma (nunca
    /// incremental) — fonte única usada por todos os hooks e pelo backfill.
    /// Fator efetivo: item com entrega fechada conta NetQuantity/Quantity (quebra de
    /// entrega devolve saldo ao contrato — semântica dos computados legados); item aberto
    /// conta o nominal. A LIBERAÇÃO soma o nominal puro (quebra não devolve saldo à
    /// liberação — ver SalesShipmentReleasesRecalculateShippedService). Em item realocado,
    /// a quebra distribui pró-rata entre os contratos que dividem o item.
    /// </summary>
    /// <summary>
    /// Fator efetivo em memória — mesma regra da projeção SQL de
    /// <see cref="CalculateAllocatedAsync"/>, para linhas ainda não persistidas.
    /// </summary>
    public static decimal EffectiveFactor(SalesInvoiceItem item) =>
        item.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed && item.Quantity != 0
            ? (item.DeliveredQuantity - item.QuantityLoss) / item.Quantity
            : 1m;

    public static async Task<decimal> CalculateAllocatedAsync(AppDbContext context, Guid salesContractKey)
    {
        var allocated = await context.SalesContractsAllocations
            .Where(a => a.SalesContractKey == salesContractKey)
            .SumAsync(a => a.Volume *
                (a.SalesInvoiceItem!.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed
                 && a.SalesInvoiceItem.Quantity != 0
                    ? (a.SalesInvoiceItem.DeliveredQuantity - a.SalesInvoiceItem.QuantityLoss)
                      / a.SalesInvoiceItem.Quantity
                    : 1m));

        return decimal.Round(allocated, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Recalcula (em memória, sem SaveChanges — o chamador persiste) o AllocatedVolume de
    /// todos os contratos com alocação nos itens informados. Usado pelos hooks em que o
    /// fator efetivo de um item muda (fechamento de entrega, quebra): o contrato fiscal do
    /// item pode não ser o único afetado — realocações espalham o item por outros contratos.
    /// Contratos encerrados são ignorados (excluídos do recálculo, como no lado de compra).
    /// </summary>
    public static async Task RecalculateForItemsAsync(AppDbContext context, ICollection<Guid> itemKeys)
    {
        if (itemKeys.Count == 0)
            return;

        var contractKeys = await context.SalesContractsAllocations
            .Where(a => itemKeys.Contains(a.SalesInvoiceItemKey))
            .Select(a => a.SalesContractKey)
            .Distinct()
            .ToListAsync();

        foreach (var contractKey in contractKeys)
        {
            var contract = await context.SalesContracts
                .FirstOrDefaultAsync(c => c.Key == contractKey);

            if (contract is null || contract.Status == ContractStatus.Finished)
                continue;

            contract.AllocatedVolume = await CalculateAllocatedAsync(context, contractKey);
        }
    }

    public async Task<SalesContractRecalcResultDto> ExecuteAsync(Guid key)
    {
        var contract = await context.SalesContracts
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Contrato não encontrado.");

        if (contract.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado não participa do recálculo de saldo.");

        var result = await RecalculateAsync(contract);
        await context.SaveChangesAsync();
        return result;
    }

    public async Task<SalesContractRecalcAllResultDto> ExecuteAllAsync()
    {
        var contracts = await context.SalesContracts
            .Where(x => x.Status != ContractStatus.Finished)
            .ToListAsync();

        var changes = new List<SalesContractRecalcResultDto>();
        foreach (var contract in contracts)
        {
            var result = await RecalculateAsync(contract);
            if (result.Changed)
                changes.Add(result);
        }

        await context.SaveChangesAsync();

        return new SalesContractRecalcAllResultDto
        {
            Scanned = contracts.Count,
            Changed = changes.Count,
            Changes = changes,
        };
    }

    private async Task<SalesContractRecalcResultDto> RecalculateAsync(SalesContract contract)
    {
        var newAllocated = await CalculateAllocatedAsync(context, contract.Key);

        var previousAllocated = contract.AllocatedVolume;
        var previousAvaiable = contract.AvaiableVolume;

        contract.AllocatedVolume = newAllocated;

        return new SalesContractRecalcResultDto
        {
            Key = contract.Key,
            Code = contract.Code,
            PreviousAllocatedVolume = previousAllocated,
            NewAllocatedVolume = newAllocated,
            PreviousAvaiableVolume = previousAvaiable,
            NewAvaiableVolume = contract.AvaiableVolume,
            Changed = previousAllocated != newAllocated,
        };
    }
}
