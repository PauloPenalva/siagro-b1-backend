using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Cria as alocações padrão do faturamento: uma linha positiva por item de invoice com
/// contrato, apontando para a liberação consumida pelo item, com snapshots de preço.
/// Chamado pelos hooks de confirmação (<c>ShipmentBillingCreateSalesInvoiceService</c> e
/// <c>SalesInvoicesConfirmService.ProcessNormalInvoiceAsync</c>) dentro da transação do
/// fluxo — por isso opera com <see cref="CommitMode.Deferred"/> sem SaveChanges próprio.
/// </summary>
public class SalesContractsAllocationCreateService(
    IUnitOfWork db,
    SalesContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteForInvoiceAsync(SalesInvoice invoice, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var items = invoice.Items
            .Where(i => i.SalesContractKey != null && i.Key != null)
            .ToList();

        if (items.Count == 0)
            return;

        // Idempotência: itens que já possuem alocação (ex.: reconfirmação) não geram linha nova.
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

        // Preço de contrato snapshotado no ledger vem da fixação Confirmed vigente
        // (fonte única do preço). Para contrato de preço fixo é igual a contract.Price
        // (a fixação automática o espelha); para PAF vem das fixações aprovadas, ou 0
        // enquanto nenhuma foi confirmada.
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
            var contract = contracts[item.SalesContractKey!.Value];
            var contractPrice = unitPrices[item.SalesContractKey!.Value];
            var allocation = new SalesContractAllocation
            {
                SalesContractKey = contract.Key,
                SalesInvoiceItemKey = item.Key!.Value,
                SalesShipmentReleaseKey = item.SalesShipmentReleaseKey,
                Volume = item.Quantity,
                InvoiceUnitPrice = item.UnitPrice,
                ContractPrice = contractPrice,
                PriceDifference = decimal.Round(
                    item.Quantity * (item.UnitPrice - contractPrice), 2, MidpointRounding.ToEven),
                Origin = SalesContractAllocationOrigin.Billing,
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

            contract.AllocatedVolume = decimal.Round(persisted + pendingSum, 3, MidpointRounding.ToEven);
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }
}
