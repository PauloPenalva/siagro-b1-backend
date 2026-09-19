using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.PurchaseDischarges;

public sealed class PurchaseDischargeSyncPlan
{
    public required Guid SalesInvoiceItemKey { get; init; }
    public required IReadOnlyList<Guid> TransactionsToCancel { get; init; }
    public required IReadOnlyList<DischargeLine> NewLines { get; init; }
    public required string? InvoiceNumber { get; init; }
    public required string? LoadCode { get; init; }
    public bool IsEmpty => TransactionsToCancel.Count == 0 && NewLines.Count == 0;
}

/// <summary>
/// Troca nota (spec §3): mantém os romaneios de quebra/sobra de descarga (16/17) de um item em
/// sincronia com a Conferência de Entregas dele. Dono único desses romaneios.
/// </summary>
/// <remarks>
/// Dois passos, porque o gancho precisa recusar ANTES de gravar o item:
/// <list type="bullet">
/// <item><see cref="PrepareAsync"/> lê e roda todas as travas, sem gravar nada;</item>
/// <item><see cref="ApplyAsync"/> cancela os romaneios ativos do item (removendo as alocações) e,
/// se a conferência ficou encerrada, cria e aloca os novos — com flush por linha, porque
/// <c>ApplyAllocationAsync</c> soma as alocações PERSISTIDAS.</item>
/// </list>
/// Nunca preenche ShipmentLoadKey/ShipmentReleaseKey/SalesInvoiceKey: os romaneios 16/17 não podem
/// entrar nos saldos da carga, da liberação nem da nota.
/// </remarks>
public class PurchaseDischargeTransactionsSyncService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageCreate,
    PurchaseContractsAllocationCreateService allocationCreate,
    PurchaseContractsAllocationDeleteService allocationDelete)
{
    public async Task<PurchaseDischargeSyncPlan> PrepareAsync(
        Guid salesInvoiceItemKey,
        SalesInvoiceDeliveryStatus newStatus,
        decimal quantity,
        decimal deliveredQuantity,
        decimal quantityLoss)
    {
        var ctx = db.Context;

        var invoice = await (
                from i in ctx.SalesInvoicesItems.AsNoTracking()
                where i.Key == salesInvoiceItemKey
                join inv in ctx.SalesInvoices.AsNoTracking() on i.SalesInvoiceKey equals inv.Key
                select new { inv.InvoiceType, inv.ShipmentLoadKey, inv.InvoiceNumber })
            .SingleOrDefaultAsync();

        var active = await ctx.StorageTransactions.AsNoTracking()
            .Where(x => x.DischargeSalesInvoiceItemKey == salesInvoiceItemKey &&
                        (x.TransactionType == StorageTransactionType.PurchaseDischargeLoss ||
                         x.TransactionType == StorageTransactionType.PurchaseDischargeGain) &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .Select(x => x.Key)
            .ToListAsync();

        var oldAllocations = await ctx.PurchaseContractsAllocations.AsNoTracking()
            .Where(a => active.Contains(a.StorageTransactionKey))
            .Select(a => new { a.PurchaseContractKey, a.Volume })
            .ToListAsync();

        IReadOnlyList<DischargeLine> lines = [];
        string? loadCode = null;

        if (invoice is { InvoiceType: SalesInvoiceType.Normal, ShipmentLoadKey: not null } &&
            newStatus == SalesInvoiceDeliveryStatus.Closed)
        {
            var loadKey = invoice.ShipmentLoadKey.Value;
            loadCode = await ctx.ShipmentLoads.AsNoTracking()
                .Where(x => x.Key == loadKey).Select(x => x.Code).FirstOrDefaultAsync();

            var shipments = await LoadShipmentsAsync(loadKey);
            lines = PurchaseDischargeAllocationPlanner.Plan(quantity, deliveredQuantity - quantityLoss, shipments);
        }

        // Travas: sobre TODO contrato mexido — os que perdem romaneio antigo e os que ganham novo.
        var contractKeys = oldAllocations.Select(a => a.PurchaseContractKey)
            .Concat(lines.Select(l => l.Shipment.PurchaseContractKey))
            .Distinct()
            .ToList();

        var contracts = await ctx.PurchaseContracts.AsNoTracking()
            .Where(c => contractKeys.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key);

        foreach (var contract in contracts.Values)
        {
            if (contract.Status == ContractStatus.Finished)
                throw new DefaultException(
                    $"O contrato de compra {contract.Code} está encerrado; reabra-o para conferir esta entrega.");

            var gain = lines
                .Where(l => l.Shipment.PurchaseContractKey == contract.Key && l.AdjustmentVolume > 0)
                .Sum(l => l.AdjustmentVolume);

            if (gain <= 0) continue;

            // Saldo depois de cancelar os antigos (remover alocação v devolve v) e aplicar os novos.
            var released = oldAllocations.Where(a => a.PurchaseContractKey == contract.Key).Sum(a => a.Volume);
            var applied = lines.Where(l => l.Shipment.PurchaseContractKey == contract.Key).Sum(l => l.AdjustmentVolume);

            if (contract.AvaiableVolume + released - applied < 0)
                throw new DefaultException(
                    $"A sobra de descarga ({gain:N3}) excede o saldo do contrato de compra {contract.Code}.");
        }

        return new PurchaseDischargeSyncPlan
        {
            SalesInvoiceItemKey = salesInvoiceItemKey,
            TransactionsToCancel = active,
            NewLines = lines,
            InvoiceNumber = invoice?.InvoiceNumber,
            LoadCode = loadCode,
        };
    }

    public async Task ApplyAsync(PurchaseDischargeSyncPlan plan, string userName)
    {
        var ctx = db.Context;

        foreach (var key in plan.TransactionsToCancel)
        {
            var allocationKeys = await ctx.PurchaseContractsAllocations
                .Where(a => a.StorageTransactionKey == key)
                .Select(a => a.Key)
                .ToListAsync();

            foreach (var allocationKey in allocationKeys)
                await allocationDelete.ExecuteAsync(allocationKey, userName, CommitMode.Auto);

            var tx = await ctx.StorageTransactions.SingleAsync(x => x.Key == key);
            tx.TransactionStatus = StorageTransactionsStatus.Cancelled;
            tx.UpdatedAt = DateTime.Now;
            tx.UpdatedBy = userName;
            await db.SaveChangesAsync();
        }

        foreach (var line in plan.NewLines)
        {
            var purchase = await ctx.StorageTransactions.AsNoTracking()
                .SingleAsync(x => x.Key == line.Shipment.PurchaseStorageTransactionKey);

            var volume = decimal.Abs(line.AdjustmentVolume);
            var isGain = line.AdjustmentVolume > 0;

            var discharge = new StorageTransaction
            {
                TransactionType = isGain
                    ? StorageTransactionType.PurchaseDischargeGain
                    : StorageTransactionType.PurchaseDischargeLoss,
                TransactionStatus = StorageTransactionsStatus.Confirmed,
                TransactionDate = DateTime.Now.Date,
                BranchCode = purchase.BranchCode,
                CardCode = purchase.CardCode,
                ItemCode = purchase.ItemCode,
                UnitOfMeasureCode = purchase.UnitOfMeasureCode,
                WarehouseCode = purchase.WarehouseCode,
                TruckCode = purchase.TruckCode,
                ProcessingCostCode = purchase.ProcessingCostCode,
                GrossWeight = volume,
                NetWeight = volume,
                AvaiableVolumeToAllocate = volume,
                DischargeSalesInvoiceItemKey = plan.SalesInvoiceItemKey,
                DischargeShippingTransactionKey = line.Shipment.ShippingTransactionKey,
                Comments = $"{(isGain ? "Sobra" : "Quebra")} de descarga — nota {plan.InvoiceNumber}, carga {plan.LoadCode}",
            };

            await storageCreate.ExecuteAsync(discharge, userName, TransactionCode.PurchaseDischarge, CommitMode.Deferred);
            await allocationCreate.ExecuteDischargeAsync(
                line.Shipment.PurchaseContractKey, discharge, volume, userName, CommitMode.Deferred);

            // Flush por linha: a próxima pode cair no MESMO contrato, e a soma lê o banco.
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Saídas(7) ativas da carga → par em SHIPPING_TRANSACTIONS → Compra(8) → alocação → contrato.
    /// Saída sem perna de compra (ou 8 sem alocação) pesa na carga, mas nunca é CIF.
    /// </summary>
    private async Task<IReadOnlyList<DischargeShipment>> LoadShipmentsAsync(Guid loadKey)
    {
        var ctx = db.Context;

        var rows = await (
                from sales in ctx.StorageTransactions.AsNoTracking()
                where sales.ShipmentLoadKey == loadKey &&
                      sales.TransactionType == StorageTransactionType.SalesShipment &&
                      sales.TransactionStatus != StorageTransactionsStatus.Cancelled
                join sht in ctx.ShippingTransactions.AsNoTracking()
                    on sales.Key equals sht.SalesStorageTransactionKey into shts
                from sht in shts.DefaultIfEmpty()
                select new
                {
                    sales.NetWeight,
                    ShippingKey = sht == null ? (Guid?)null : sht.Key,
                    PurchaseKey = sht == null ? null : sht.PurchaseStorageTransactionKey,
                })
            .ToListAsync();

        var purchaseKeys = rows.Where(r => r.PurchaseKey != null).Select(r => r.PurchaseKey!.Value).ToList();

        var contractsByPurchase = (await (
                    from a in ctx.PurchaseContractsAllocations.AsNoTracking()
                    where purchaseKeys.Contains(a.StorageTransactionKey)
                    join c in ctx.PurchaseContracts.AsNoTracking() on a.PurchaseContractKey equals c.Key
                    select new { a.StorageTransactionKey, ContractKey = c.Key, c.FreightTerms })
                .ToListAsync())
            .GroupBy(x => x.StorageTransactionKey)
            .ToDictionary(g => g.Key, g => g.First());

        return rows.Select(r =>
        {
            var contract = r.PurchaseKey != null && contractsByPurchase.TryGetValue(r.PurchaseKey.Value, out var c)
                ? c
                : null;

            return new DischargeShipment(
                r.ShippingKey ?? Guid.Empty,
                r.PurchaseKey ?? Guid.Empty,
                contract?.ContractKey ?? Guid.Empty,
                r.NetWeight,
                IsCif: contract is { FreightTerms: FreightTerms.Cif } && r.ShippingKey != null);
        }).ToList();
    }
}
