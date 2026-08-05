using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Materializa os efeitos da natureza de operação de um documento de saída AVULSO como linha
/// do ledger SALES_CONTRACTS_ALLOCATIONS, origem
/// <see cref="SalesContractAllocationOrigin.FiscalAdjustment"/>. Nenhum mecanismo novo e
/// nenhuma coluna nova no contrato: o ledger já é a fonte única do consumo.
///
/// Saldo (<see cref="ContractBalanceEffect"/>): Consume → Volume positivo; Restore →
/// negativo; None → nenhuma linha de volume.
/// Valor (<see cref="ContractValueEffect"/>): Add → PriceDifference positivo; Subtract →
/// negativo; None → zero.
///
/// O complemento de preço (None/Add) grava Volume = 0: não toca o saldo físico e não altera a
/// invariante "Σ Volume por item = consumo nominal do item". O valor da linha é sempre
/// Quantidade × Preço unitário — no complemento, a quantidade complementada vezes a DIFERENÇA
/// de preço unitária, que é como a NF complementar é emitida.
///
/// <see cref="SalesContractAllocation.SalesShipmentReleaseKey"/> fica NULL de propósito:
/// ajuste fiscal não consome liberação de entrega, e linha negativa amarrada a uma liberação
/// inflaria o saldo físico do contrato.
///
/// Roda dentro da transação do chamador quando invocado com
/// <see cref="CommitMode.Deferred"/> — sem SaveChanges próprio.
/// </summary>
public class SalesContractsAllocationCreateForFiscalAdjustmentService(
    IUnitOfWork db,
    SalesContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteAsync(SalesInvoice invoice,
        IReadOnlyList<SalesInvoiceItemUsage> lineUsages, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        // A natureza é de LINHA: cada item traz o próprio efeito. Linha sem efeito nenhum
        // não gera ledger, e um mesmo documento pode ter linha que consome saldo ao lado de
        // linha que só complementa valor.
        var usageByItem = lineUsages
            .Where(l => l.Usage.ContractBalanceEffect != ContractBalanceEffect.None
                        || l.Usage.ContractValueEffect != ContractValueEffect.None)
            .ToDictionary(l => l.Item, l => l.Usage);

        var items = usageByItem.Keys
            .Where(i => i.SalesContractKey != null && i.Key != null)
            .ToList();

        if (items.Count == 0)
            return;

        // Idempotência: item que já tem linha (reconfirmação) não gera outra.
        var itemKeys = items.Select(i => i.Key!.Value).ToList();
        var alreadyAllocated = await db.Context.SalesContractsAllocations
            .Where(a => itemKeys.Contains(a.SalesInvoiceItemKey))
            .Select(a => a.SalesInvoiceItemKey)
            .Distinct()
            .ToListAsync();

        items = items.Where(i => !alreadyAllocated.Contains(i.Key!.Value)).ToList();
        if (items.Count == 0)
            return;

        var contractKeys = items.Select(i => i.SalesContractKey!.Value).Distinct().ToList();
        var contracts = await db.Context.SalesContracts
            .Where(c => contractKeys.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key);

        var unitPrices = new Dictionary<Guid, decimal>();
        foreach (var contractKey in contractKeys)
        {
            if (!contracts.TryGetValue(contractKey, out var contract))
                throw new ApplicationException("Contrato de venda não encontrado.");

            if (contract.Status == ContractStatus.Finished)
                throw new ApplicationException("Contrato encerrado: não é possível alocar.");

            unitPrices[contractKey] =
                await fixedVolumeService.ConfirmedUnitPriceAsync(contractKey, contract.Price);
        }

        var pending = new List<SalesContractAllocation>();
        foreach (var item in items)
        {
            var contractKey = item.SalesContractKey!.Value;
            var usage = usageByItem[item];

            var volume = usage.ContractBalanceEffect switch
            {
                ContractBalanceEffect.Consume => item.Quantity,
                ContractBalanceEffect.Restore => -item.Quantity,
                _ => 0m,
            };

            var lineValue = decimal.Round(
                item.Quantity * item.UnitPrice, 2, MidpointRounding.ToEven);

            var priceDifference = usage.ContractValueEffect switch
            {
                ContractValueEffect.Add => lineValue,
                ContractValueEffect.Subtract => -lineValue,
                _ => 0m,
            };

            var allocation = new SalesContractAllocation
            {
                SalesContractKey = contractKey,
                SalesInvoiceItemKey = item.Key!.Value,
                SalesShipmentReleaseKey = null,
                Volume = volume,
                InvoiceUnitPrice = item.UnitPrice,
                ContractPrice = unitPrices[contractKey],
                PriceDifference = priceDifference,
                Origin = SalesContractAllocationOrigin.FiscalAdjustment,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            };

            pending.Add(allocation);
            await db.Context.SalesContractsAllocations.AddAsync(allocation);
        }

        // Derivado-da-soma (nunca incremental): Σ do banco + linhas pendentes desta chamada.
        foreach (var contractKey in contractKeys)
        {
            var contract = contracts[contractKey];
            var persisted = await SalesContractsRecalculateBalanceService
                .CalculateAllocatedAsync(db.Context, contractKey);
            var pendingSum = pending
                .Where(a => a.SalesContractKey == contractKey)
                .Sum(a => a.Volume * SalesContractsRecalculateBalanceService.EffectiveFactor(
                    items.First(i => i.Key!.Value == a.SalesInvoiceItemKey)));

            contract.AllocatedVolume = decimal.Round(
                persisted + pendingSum, 3, MidpointRounding.ToEven);
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }
}
