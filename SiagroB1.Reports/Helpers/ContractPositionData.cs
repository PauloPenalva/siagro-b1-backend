using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Leitura dos contratos e do razão para os relatórios de posição. As fórmulas de entregue e saldo
/// são AS MESMAS dos serviços do SiagroB1.Application (que o Reports não pode referenciar):
/// <c>PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync</c>,
/// <c>PurchaseContractsWashedOutVolumeService.ActiveVolumesAsync</c> e
/// <c>SalesContractsRecalculateBalanceService.CalculateAllocatedAsync</c> — só que somadas em lote
/// (GROUP BY por contrato) em vez de uma consulta por contrato. <c>ContractPositionDataTests</c>
/// compara as duas contas sobre o mesmo dado: se a regra mudar lá, o teste daqui quebra.
/// Nada de JOIN com ITEMS/BUSINESS_PARTNERS (vazias em SAPB1): só snapshots do contrato.
/// </summary>
public static class ContractPositionData
{
    /// <summary>Soma por contrato (projeção traduzível para SQL).</summary>
    public sealed class KeyedSum
    {
        public Guid Key { get; init; }
        public decimal Sum { get; init; }
    }

    public static IQueryable<PurchaseContract> PurchaseQuery(AppDbContext context, ContractPositionReportRequest request)
    {
        var statuses = ContractPositionText.QueryStatuses(request.Statuses);
        var query = context.PurchaseContracts.AsNoTracking().Where(c => statuses.Contains(c.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(c => c.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(c => c.ItemCode == request.ItemCode);
        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            query = query.Where(c => c.HarvestSeasonCode == request.HarvestSeasonCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(c => c.CardCode == request.CardCode);
        if (request.Type is { } type)
            query = query.Where(c => c.Type == type);
        if (request.DeliveryEndDateUntil is { } until)
        {
            var untilExclusive = until.Date.AddDays(1);
            query = query.Where(c => c.DeliveryEndDate >= ContractPositionText.NoDeadlineBefore
                                     && c.DeliveryEndDate < untilExclusive);
        }

        return query;
    }

    public static IQueryable<SalesContract> SalesQuery(AppDbContext context, ContractPositionReportRequest request)
    {
        var statuses = ContractPositionText.QueryStatuses(request.Statuses);
        var query = context.SalesContracts.AsNoTracking().Where(c => statuses.Contains(c.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(c => c.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(c => c.ItemCode == request.ItemCode);
        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            query = query.Where(c => c.HarvestSeasonCode == request.HarvestSeasonCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(c => c.CardCode == request.CardCode);
        if (request.Type is { } type)
            query = query.Where(c => c.Type == type);
        if (request.DeliveryEndDateUntil is { } until)
        {
            var untilExclusive = until.Date.AddDays(1);
            query = query.Where(c => c.DeliveryEndDate >= ContractPositionText.NoDeadlineBefore
                                     && c.DeliveryEndDate < untilExclusive);
        }

        return query;
    }

    /// <summary>Σ Volume COM SINAL das alocações (devolução = negativo) — igual ao recálculo de compra.</summary>
    public static IQueryable<KeyedSum> PurchaseDeliveredQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.PurchaseContractsAllocations
            .Where(a => contractKeys.Contains(a.PurchaseContractKey))
            .GroupBy(a => a.PurchaseContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(a => a.Volume) });

    /// <summary>Σ (fixado + não fixado) dos washouts ATIVOS (em aprovação + aprovado).</summary>
    public static IQueryable<KeyedSum> PurchaseWashedOutQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.PurchaseContractsWashouts
            .Where(w => contractKeys.Contains(w.PurchaseContractKey)
                        && (w.Status == PurchaseContractWashoutStatus.InApproval
                            || w.Status == PurchaseContractWashoutStatus.Approved))
            .GroupBy(w => w.PurchaseContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(w => w.FixedVolume + w.UnfixedVolume) });

    /// <summary>Σ Volume COM SINAL das alocações de venda (nominal).</summary>
    public static IQueryable<KeyedSum> SalesNominalQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.SalesContractsAllocations
            .Where(a => contractKeys.Contains(a.SalesContractKey))
            .GroupBy(a => a.SalesContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(a => a.Volume) });

    /// <summary>
    /// Quebra apurada: só a linha DONA da diferença, e só com a entrega do item conferida
    /// (Closed): Quantity − (DeliveredQuantity − QuantityLoss). Projeta antes de agrupar para o
    /// SQL Server traduzir a navegação.
    /// </summary>
    public static IQueryable<KeyedSum> SalesShortageQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.SalesContractsAllocations
            .Where(a => contractKeys.Contains(a.SalesContractKey)
                        && a.OwnsDeliveryDifference
                        && a.SalesInvoiceItem!.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed)
            .Select(a => new
            {
                a.SalesContractKey,
                Shortage = a.SalesInvoiceItem!.Quantity
                           - (a.SalesInvoiceItem.DeliveredQuantity - a.SalesInvoiceItem.QuantityLoss),
            })
            .GroupBy(x => x.SalesContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(x => x.Shortage) });

    public static async Task<List<ContractPosition>> PurchasePositionsAsync(
        AppDbContext context, IQueryable<PurchaseContract> query)
    {
        var contracts = await query.ToListAsync();
        if (contracts.Count == 0)
            return [];

        var keys = query.Select(c => c.Key);
        var delivered = await ToDictionaryAsync(PurchaseDeliveredQuery(context, keys));
        var washedOut = await ToDictionaryAsync(PurchaseWashedOutQuery(context, keys));

        return contracts.Select(c =>
        {
            var contractDelivered = delivered.GetValueOrDefault(c.Key);
            var contractWashedOut = Round3(washedOut.GetValueOrDefault(c.Key));
            return new ContractPosition
            {
                Side = ContractPositionSide.Purchase,
                Key = c.Key,
                Code = c.Code ?? "",
                CreationDate = c.CreationDate,
                CardCode = c.CardCode,
                CardName = c.CardName,
                ItemCode = c.ItemCode,
                ItemName = c.ItemName,
                UnitOfMeasureCode = c.UnitOfMeasureCode,
                HarvestSeasonCode = c.HarvestSeasonCode,
                Type = c.Type,
                Status = c.Status,
                StandardCashFlowDate = c.StandardCashFlowDate,
                DeliveryEndDate = c.DeliveryEndDate,
                Price = c.StandardPrice,
                Contracted = c.TotalVolume,
                Delivered = contractDelivered,
                WashedOut = contractWashedOut,
                // = PurchaseContract.AvaiableVolume com AllocatedVolume e WashedOutVolume recalculados.
                Balance = decimal.Round(c.TotalVolume - contractDelivered - contractWashedOut, 2, MidpointRounding.ToEven),
            };
        }).ToList();
    }

    public static async Task<List<ContractPosition>> SalesPositionsAsync(
        AppDbContext context, IQueryable<SalesContract> query)
    {
        var contracts = await query.ToListAsync();
        if (contracts.Count == 0)
            return [];

        var keys = query.Select(c => c.Key);
        var nominal = await ToDictionaryAsync(SalesNominalQuery(context, keys));
        var shortage = await ToDictionaryAsync(SalesShortageQuery(context, keys));

        return contracts.Select(c =>
        {
            var contractDelivered = Round3(nominal.GetValueOrDefault(c.Key) - shortage.GetValueOrDefault(c.Key));
            return new ContractPosition
            {
                Side = ContractPositionSide.Sales,
                Key = c.Key,
                Code = c.Code ?? "",
                CreationDate = c.CreationDate,
                CardCode = c.CardCode,
                CardName = c.CardName,
                ItemCode = c.ItemCode,
                ItemName = c.ItemName,
                UnitOfMeasureCode = c.UnitOfMeasureCode,
                HarvestSeasonCode = c.HarvestSeasonCode,
                Type = c.Type,
                Status = c.Status,
                StandardCashFlowDate = c.StandardCashFlowDate,
                DeliveryEndDate = c.DeliveryEndDate,
                Price = c.Price,
                Contracted = c.TotalVolume,
                Delivered = contractDelivered,
                WashedOut = 0m,
                // = SalesContract.AvaiableVolume com AllocatedVolume recalculado.
                Balance = Round3(c.TotalVolume - contractDelivered),
            };
        }).ToList();
    }

    private static async Task<Dictionary<Guid, decimal>> ToDictionaryAsync(IQueryable<KeyedSum> query) =>
        (await query.ToListAsync()).ToDictionary(s => s.Key, s => s.Sum);

    private static decimal Round3(decimal value) => decimal.Round(value, 3, MidpointRounding.ToEven);
}
