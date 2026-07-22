using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Realoca volume faturado de um item de invoice entre contratos de VENDA: grava um par
/// de linhas −/+ no ledger (amarradas por ReallocationGroupKey), devolvendo saldo ao
/// contrato/liberação de origem e consumindo o contrato/liberação de destino. A linha
/// positiva apura a diferença de preço entre o preço faturado e o preço do contrato de
/// destino; a negativa reverte a diferença apurada na origem. Nada é emitido/lançado
/// financeiramente — a diferença é apenas apurada e exposta.
/// </summary>
public class SalesContractsReallocationCreateService(
    IUnitOfWork db,
    SalesShipmentReleaseMovementGuardService movementGuard)
{
    public async Task ExecuteWithTransactionAsync(Guid salesInvoiceItemKey, Guid sourceSalesContractKey,
        Guid targetSalesContractKey, Guid targetSalesShipmentReleaseKey, decimal volume, string userName)
    {
        try
        {
            await db.BeginTransactionAsync();
            await ExecuteAsync(salesInvoiceItemKey, sourceSalesContractKey, targetSalesContractKey,
                targetSalesShipmentReleaseKey, volume, userName);
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw new DefaultException(e.Message);
        }
    }

    public async Task ExecuteAsync(Guid salesInvoiceItemKey, Guid sourceSalesContractKey,
        Guid targetSalesContractKey, Guid targetSalesShipmentReleaseKey, decimal volume, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var item = await db.Context.SalesInvoicesItems
                       .Include(i => i.SalesInvoice)
                       .FirstOrDefaultAsync(i => i.Key == salesInvoiceItemKey)
                   ?? throw new NotFoundException("Item da nota não encontrado.");

        if (item.SalesInvoice?.InvoiceStatus != InvoiceStatus.Confirmed
            || item.SalesInvoice.InvoiceType != SalesInvoiceType.Normal)
            throw new ApplicationException("Somente notas confirmadas do tipo Normal podem ser realocadas.");

        if (sourceSalesContractKey == targetSalesContractKey)
            throw new ApplicationException("O contrato de destino deve ser diferente do contrato de origem.");

        var source = await db.Context.SalesContracts
                         .FirstOrDefaultAsync(c => c.Key == sourceSalesContractKey)
                     ?? throw new NotFoundException("Contrato de origem não encontrado.");
        var target = await db.Context.SalesContracts
                         .FirstOrDefaultAsync(c => c.Key == targetSalesContractKey)
                     ?? throw new NotFoundException("Contrato de destino não encontrado.");

        if (source.Status == ContractStatus.Finished || target.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível realocar.");

        if (target.CardCode != item.SalesInvoice.CardCode)
            throw new ApplicationException("O contrato de destino pertence a outro cliente.");

        if (target.ItemCode != item.ItemCode)
            throw new ApplicationException("O contrato de destino é de outro produto.");

        if (target.UnitOfMeasureCode != item.UnitOfMeasureCode)
            throw new ApplicationException("O contrato de destino usa outra unidade de medida.");

        if (volume <= 0)
            throw new ApplicationException("Volume inválido para realocação.");

        // Saldo alocado do item no contrato de origem, por (liberação): família do item =
        // linhas do próprio item (faturamento + realocações) + linhas de devoluções que
        // apontam para ele. Nota: safra NÃO bloqueia — a conciliação com o relatório de
        // entrega do cliente pode cruzar safras (espelha o lado de compra).
        var sourceGroups = await db.Context.SalesContractsAllocations
            .Where(a => a.SalesContractKey == sourceSalesContractKey &&
                        (a.SalesInvoiceItemKey == salesInvoiceItemKey ||
                         a.SalesInvoiceItem!.SalesInvoiceItemOriginKey == salesInvoiceItemKey))
            .GroupBy(a => a.SalesShipmentReleaseKey)
            .Select(g => new { ReleaseKey = g.Key, Net = g.Sum(a => a.Volume) })
            .ToListAsync();

        var positiveGroups = sourceGroups.Where(g => g.Net > 0).OrderByDescending(g => g.Net).ToList();
        var availableAtSource = positiveGroups.Sum(g => g.Net);

        if (volume > availableAtSource)
            throw new ApplicationException(
                $"O volume informado é superior ao saldo alocado da nota no contrato de origem " +
                $"({availableAtSource:N3}).");

        var targetRelease = await db.Context.SalesShipmentReleases
                                .FirstOrDefaultAsync(r => r.Key == targetSalesShipmentReleaseKey)
                            ?? throw new NotFoundException("Liberação de entrega não encontrada.");

        if (targetRelease.SalesContractKey != targetSalesContractKey)
            throw new ApplicationException("A liberação informada não pertence ao contrato de destino.");

        await movementGuard.EnsureCanBillAsync(targetSalesShipmentReleaseKey);

        if (targetRelease.Status != ReleaseStatus.Actived)
            throw new ApplicationException("Liberação de entrega ainda não aprovada: não é possível realocar.");

        if (volume > targetRelease.AvailableQuantity)
            throw new ApplicationException(
                $"Saldo da liberação de destino insuficiente ({targetRelease.AvailableQuantity:N3}).");

        if (volume > target.AvaiableVolume)
            throw new ApplicationException(
                $"Saldo do contrato de destino insuficiente ({target.AvaiableVolume:N3}).");

        var groupKey = Guid.NewGuid();
        var pending = new List<SalesContractAllocation>();

        // Linhas negativas na origem: consumo greedy dos grupos com saldo (na prática, uma
        // liberação por nota no faturamento). Reverte pró-rata a diferença apurada na origem.
        var remaining = volume;
        foreach (var group in positiveGroups)
        {
            if (remaining <= 0)
                break;

            var slice = Math.Min(remaining, group.Net);
            remaining -= slice;

            pending.Add(new SalesContractAllocation
            {
                SalesContractKey = sourceSalesContractKey,
                SalesInvoiceItemKey = salesInvoiceItemKey,
                SalesShipmentReleaseKey = group.ReleaseKey,
                Volume = -slice,
                InvoiceUnitPrice = item.UnitPrice,
                ContractPrice = source.Price,
                PriceDifference = decimal.Round(
                    -slice * (item.UnitPrice - source.Price), 2, MidpointRounding.ToEven),
                Origin = SalesContractAllocationOrigin.Reallocation,
                ReallocationGroupKey = groupKey,
                CounterpartySalesContractKey = targetSalesContractKey,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            });
        }

        // Linha positiva no destino: consome a liberação do destino e apura a diferença
        // entre o preço faturado e o preço do contrato de destino.
        pending.Add(new SalesContractAllocation
        {
            SalesContractKey = targetSalesContractKey,
            SalesInvoiceItemKey = salesInvoiceItemKey,
            SalesShipmentReleaseKey = targetSalesShipmentReleaseKey,
            Volume = volume,
            InvoiceUnitPrice = item.UnitPrice,
            ContractPrice = target.Price,
            PriceDifference = decimal.Round(
                volume * (item.UnitPrice - target.Price), 2, MidpointRounding.ToEven),
            Origin = SalesContractAllocationOrigin.Reallocation,
            ReallocationGroupKey = groupKey,
            CounterpartySalesContractKey = sourceSalesContractKey,
            ApprovedAt = DateTime.Now,
            ApprovedBy = userName,
        });

        await db.Context.SalesContractsAllocations.AddRangeAsync(pending);

        // Recalcs derivado-da-soma (banco + pendentes desta chamada).
        var factor = SalesContractsRecalculateBalanceService.EffectiveFactor(item);
        foreach (var contract in new[] { source, target })
        {
            var persisted = await SalesContractsRecalculateBalanceService
                .CalculateAllocatedAsync(db.Context, contract.Key);
            var pendingSum = pending
                .Where(a => a.SalesContractKey == contract.Key)
                .Sum(a => a.Volume * factor);

            contract.AllocatedVolume = decimal.Round(persisted + pendingSum, 3, MidpointRounding.ToEven);
        }

        var affectedReleaseKeys = pending
            .Where(a => a.SalesShipmentReleaseKey != null)
            .Select(a => a.SalesShipmentReleaseKey!.Value)
            .Distinct()
            .ToList();

        foreach (var releaseKey in affectedReleaseKeys)
        {
            var release = releaseKey == targetSalesShipmentReleaseKey
                ? targetRelease
                : await db.Context.SalesShipmentReleases.FirstOrDefaultAsync(r => r.Key == releaseKey);
            if (release is null)
                continue;

            var persisted = await db.Context.SalesContractsAllocations
                .Where(a => a.SalesShipmentReleaseKey == releaseKey)
                .SumAsync(a => a.Volume);
            var pendingSum = pending
                .Where(a => a.SalesShipmentReleaseKey == releaseKey)
                .Sum(a => a.Volume);

            release.ShippedQuantity = persisted + pendingSum;
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }
}
