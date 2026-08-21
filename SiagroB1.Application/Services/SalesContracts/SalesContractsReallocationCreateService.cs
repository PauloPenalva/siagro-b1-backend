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
///
/// <b>Liberação de destino</b> — <c>targetSalesShipmentReleaseKey</c>:
/// <list type="bullet">
/// <item><b>Informada</b>: consome exatamente essa liberação, com os guards operacionais
/// (pertence ao destino, ATIVA, saldo suficiente). Caminho de compatibilidade.</item>
/// <item><b>Nula</b>: o servidor escolhe sozinho, consumindo as liberações ATIVAS do
/// destino em ordem de <c>ReleaseDate</c> (FIFO) até cobrir o volume. O que sobrar vira
/// uma linha sem liberação — formato das linhas legadas, e o único caminho possível para
/// contrato que nunca teve liberação. Fecha o vazamento em que o ajuste devolvia saldo à
/// liberação de ORIGEM sem consumir nenhuma no destino.</item>
/// </list>
///
/// <b>Saldo do destino</b> — <c>allowNegativeBalance</c>: por padrão o volume não pode
/// exceder o saldo do contrato de destino. Com a flag, pode — e exige motivo. É a flag,
/// não a ausência de liberação, que define a <see cref="SalesContractAllocationOrigin"/>:
/// quem furou a invariante grava <c>Reconciliation</c>, o resto grava <c>Reallocation</c>.
/// Furar o saldo é o que destrava a TROCA CRUZADA (a nota de A pertence ao B e a de B
/// pertence ao A, ambos zerados), em que cada movimento exigiria o outro antes.
/// </summary>
public class SalesContractsReallocationCreateService(
    IUnitOfWork db,
    SalesShipmentReleaseMovementGuardService movementGuard)
{
    public async Task ExecuteWithTransactionAsync(Guid salesInvoiceItemKey, Guid sourceSalesContractKey,
        Guid targetSalesContractKey, Guid? targetSalesShipmentReleaseKey, decimal volume, string userName,
        string? reconciliationReason = null, bool allowNegativeBalance = false)
    {
        try
        {
            await db.BeginTransactionAsync();
            await ExecuteAsync(salesInvoiceItemKey, sourceSalesContractKey, targetSalesContractKey,
                targetSalesShipmentReleaseKey, volume, userName, reconciliationReason, allowNegativeBalance);
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw new DefaultException(e.Message);
        }
    }

    public async Task ExecuteAsync(Guid salesInvoiceItemKey, Guid sourceSalesContractKey,
        Guid targetSalesContractKey, Guid? targetSalesShipmentReleaseKey, decimal volume, string userName,
        string? reconciliationReason = null, bool allowNegativeBalance = false,
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

        // Cliente do destino NÃO é validado: conciliar para o contrato de outro cliente é
        // caso de uso desta tela — a conferência com o relatório de entrega revela notas
        // que pertencem ao contrato de outra empresa. Produto e unidade de medida seguem
        // travados, porque aí o volume deixaria de ser comparável.
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

        // Saldo do contrato de destino: quem decide é a FLAG, não a presença de liberação.
        // Furar o saldo é a exceção auditável — por isso exige motivo e marca a origem.
        SalesContractAllocationOrigin origin;

        if (allowNegativeBalance)
        {
            if (string.IsNullOrWhiteSpace(reconciliationReason))
                throw new ApplicationException("Informe o motivo da conciliação.");

            origin = SalesContractAllocationOrigin.Reconciliation;
        }
        else
        {
            if (volume > target.AvaiableVolume)
                throw new ApplicationException(
                    $"Saldo do contrato de destino insuficiente ({target.AvaiableVolume:N3}).");

            origin = SalesContractAllocationOrigin.Reallocation;
        }

        // Fatias do destino: (liberação consumida, volume). Chave nula = volume estacionado
        // sem liberação, no mesmo formato das linhas legadas.
        var targetSlices = new List<(Guid? ReleaseKey, decimal Volume)>();
        var targetReleases = new Dictionary<Guid, SalesShipmentRelease>();

        if (targetSalesShipmentReleaseKey is not null)
        {
            var targetRelease = await db.Context.SalesShipmentReleases
                                    .FirstOrDefaultAsync(r => r.Key == targetSalesShipmentReleaseKey)
                                ?? throw new NotFoundException("Liberação de entrega não encontrada.");

            if (targetRelease.SalesContractKey != targetSalesContractKey)
                throw new ApplicationException("A liberação informada não pertence ao contrato de destino.");

            await movementGuard.EnsureCanBillAsync(targetSalesShipmentReleaseKey.Value);

            if (targetRelease.Status != ReleaseStatus.Actived)
                throw new ApplicationException("Liberação de entrega ainda não aprovada: não é possível realocar.");

            if (volume > targetRelease.AvailableQuantity)
                throw new ApplicationException(
                    $"Saldo da liberação de destino insuficiente ({targetRelease.AvailableQuantity:N3}).");

            targetSlices.Add((targetRelease.Key, volume));
            targetReleases[targetRelease.Key] = targetRelease;
        }
        else
        {
            // FIFO por data de liberação: consome as ativas com saldo, da mais antiga para
            // a mais nova. Sem isso o ajuste devolveria saldo à liberação de origem sem
            // consumir nenhuma no destino, inflando saldo de liberação a cada conciliação.
            var fifo = await db.Context.SalesShipmentReleases
                .Where(r => r.SalesContractKey == targetSalesContractKey
                            && r.Status == ReleaseStatus.Actived
                            && (r.ReleasedQuantity - r.ShippedQuantity) > 0)
                .OrderBy(r => r.ReleaseDate)
                .ThenBy(r => r.RowId)
                .ToListAsync();

            var toPlace = volume;
            foreach (var release in fifo)
            {
                if (toPlace <= 0)
                    break;

                var slice = Math.Min(toPlace, release.AvailableQuantity);
                if (slice <= 0)
                    continue;

                toPlace -= slice;
                targetSlices.Add((release.Key, slice));
                targetReleases[release.Key] = release;
            }

            // Contrato sem liberação (ou com liberação insuficiente) continua sendo destino
            // válido — é o caso dos contratos legados, que nunca tiveram liberação.
            if (toPlace > 0)
                targetSlices.Add((null, toPlace));
        }

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
                Origin = origin,
                ReallocationGroupKey = groupKey,
                CounterpartySalesContractKey = targetSalesContractKey,
                ReconciliationReason = reconciliationReason,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            });
        }

        // Linhas positivas no destino: uma por fatia (liberação consumida no FIFO, mais o
        // eventual resto sem liberação). Cada fatia apura a sua parte da diferença entre o
        // preço faturado e o preço do contrato de destino.
        foreach (var (releaseKey, sliceVolume) in targetSlices)
        {
            pending.Add(new SalesContractAllocation
            {
                SalesContractKey = targetSalesContractKey,
                SalesInvoiceItemKey = salesInvoiceItemKey,
                SalesShipmentReleaseKey = releaseKey,
                Volume = sliceVolume,
                InvoiceUnitPrice = item.UnitPrice,
                ContractPrice = target.Price,
                PriceDifference = decimal.Round(
                    sliceVolume * (item.UnitPrice - target.Price), 2, MidpointRounding.ToEven),
                Origin = origin,
                ReallocationGroupKey = groupKey,
                CounterpartySalesContractKey = sourceSalesContractKey,
                ReconciliationReason = reconciliationReason,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            });
        }

        await db.Context.SalesContractsAllocations.AddRangeAsync(pending);

        // Titularidade da diferença de entrega sobre o conjunto PÓS-MUTAÇÃO do item: se estas
        // linhas zeram o líquido do contrato dono (troca cruzada, em que a nota inteira muda
        // de contrato), a quebra ACOMPANHA O VOLUME para o destino em vez de ficar sozinha
        // na origem.
        var itemLines = await db.Context.SalesContractsAllocations
            .Where(a => a.SalesInvoiceItemKey == salesInvoiceItemKey)
            .ToListAsync();
        itemLines.AddRange(pending);
        SalesContractsDeliveryDifferenceOwnerService.EnsureOwner(itemLines);

        await SalesContractsRecalculateBalanceService.RecalculateForTouchedItemAsync(
            db.Context, salesInvoiceItemKey, item, itemLines,
            extraContractKeys: [source.Key, target.Key]);

        // Mesma regra e mesmo conjunto pós-mutação do contrato: a liberação também consome o
        // líquido quando a entrega já foi conferida. As de destino já estão rastreadas por
        // targetReleases; o helper reencontra a mesma instância pelo identity map.
        await SalesShipmentReleasesRecalculateShippedService.RecalculateForTouchedItemAsync(
            db.Context, salesInvoiceItemKey, item, itemLines);

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }
}
