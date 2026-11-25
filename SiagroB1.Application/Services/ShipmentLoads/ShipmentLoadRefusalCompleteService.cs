using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Segundo tempo da recusa de carga na filial que emite NF-e pelo Siagro (spec 2026-10-09 §5.3). Chamado por
/// <c>SalesInvoicesConfirmService</c> a cada devolução confirmada e por <c>SalesInvoicesCancelService</c> quando o 2b
/// cancela a NF-e de uma devolução da recusa; quando TODAS as devoluções vivas da recusa estão confirmadas, aplica os
/// efeitos do destino e conclui a recusa — na transação de quem chamou.
/// </summary>
/// <remarks>
/// Falha aqui derruba a confirmação inteira; <c>NfeResultHandlerBase</c> mantém a NF-e Autorizada, grava o erro e o
/// "Concluir confirmação" refaz tudo. A quantidade é a soma das devoluções CONFIRMADAS: uma devolução cuja NF-e foi
/// cancelada pelo 2b sai da conta (Review Focus 3). Sem nenhuma devolução viva, a recusa não tem mais o que concluir e
/// é cancelada — senão a carga ficaria travada para sempre.
/// </remarks>
public class ShipmentLoadRefusalCompleteService(
    IUnitOfWork db,
    ShipmentLoadRefusalEffectsService effects,
    ShipmentLoadsMovementLogService movementLog)
{
    public Task ApplyAsync(SalesInvoice confirmedReturn, string userName) =>
        confirmedReturn.ShipmentLoadRefusalKey is { } refusalKey
            ? TryCompleteAsync(refusalKey, userName)
            : Task.CompletedTask;

    /// <summary>
    /// Reavalia a recusa Pendente: sem devolução viva → cancela; todas as vivas confirmadas → conclui; senão, nada.
    /// Não abre nem comita transação (roda dentro da de quem chama).
    /// </summary>
    public async Task TryCompleteAsync(Guid refusalKey, string userName)
    {
        var refusal = await db.Context.ShipmentLoadRefusals.FirstOrDefaultAsync(x => x.Key == refusalKey);

        if (refusal is not { Status: ShipmentLoadRefusalStatus.Pending })
            return;

        var returns = await db.Context.SalesInvoices
            .Include(i => i.Items)
            .Where(i => i.ShipmentLoadRefusalKey == refusalKey && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .ToListAsync();

        if (returns.Count == 0)
        {
            await CancelAsync(refusal, userName);
            return;
        }

        if (returns.Any(i => i.InvoiceStatus != InvoiceStatus.Confirmed))
            return;

        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == refusal.ShipmentLoadKey);

        var originKeys = returns
            .Where(i => i.SalesInvoiceOriginKey != null)
            .Select(i => i.SalesInvoiceOriginKey)
            .Distinct()
            .ToList();
        var origins = await db.Context.SalesInvoices.Where(i => originKeys.Contains(i.Key)).ToListAsync();

        var totalQuantity = decimal.Round(returns.SelectMany(i => i.Items).Sum(i => i.Quantity), 3, MidpointRounding.ToEven);

        // Só a regra de "não empilhar": EnsureLoadAcceptsTransshipment recusaria a carga travada pela própria recusa.
        if (refusal.Destination == RefusalDestination.Transshipment)
            await ShipmentLoadTransshipmentRules.EnsureIsLastAsync(db.Context, load);

        // A recusa sai de Pending ANTES dos efeitos: os recálculos lá dentro precisam ver a carga destravada.
        refusal.Status = ShipmentLoadRefusalStatus.Completed;
        refusal.CompletedAt = DateTime.Now;
        refusal.CompletedBy = userName;
        await db.SaveChangesAsync();

        if (origins.Count > 0 && totalQuantity > decimal.Zero)
        {
            // GAC-1226: o ajuste do efetivo é calculado AQUI, quando os efeitos são aplicados, pelos registros Open
            // deste momento — o físico do documento divergente devolvido por inteiro é o efetivo, não a NF.
            var refusedDocuments = new List<(Guid SalesInvoiceKey, decimal RemainingAfterRefusal)>();
            foreach (var origin in origins)
                refusedDocuments.Add((origin.Key, await RemainingAfterReturnsAsync(origin.Key)));

            var physicalQuantity = await ShipmentLoadRefusalRules.PhysicalQuantityAsync(
                db.Context, refusal.Destination, totalQuantity, refusedDocuments);

            await effects.ApplyAsync(
                new RefusalEffectsInput(
                    load, refusal.Destination, refusal.DestinationWarehouseCode, refusal.DestinationWarehouseName,
                    origins, physicalQuantity, refusal.Reason, refusal.TransitLossReason, returns),
                userName);
        }

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
        load.UpdatedAt = DateTime.Now;
        load.UpdatedBy = userName;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// O que resta a devolver do documento de origem, depois das devoluções vivas (não canceladas) — o mesmo critério
    /// de <c>SalesInvoicesConfirmService.IsFullyReturnedAsync</c>, que marcou a origem como devolvida: item a item,
    /// faturado menos devolvido, sem deixar um item devolvido a mais compensar outro.
    /// </summary>
    private async Task<decimal> RemainingAfterReturnsAsync(Guid originKey)
    {
        var originItems = await db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == originKey)
            .Select(x => new { x.Key, x.Quantity })
            .ToListAsync();

        var remaining = decimal.Zero;

        foreach (var originItem in originItems)
        {
            var returned = await db.Context.SalesInvoicesItems
                .AsNoTracking()
                .Where(x => x.SalesInvoiceItemOriginKey == originItem.Key &&
                            x.SalesInvoice!.InvoiceType == SalesInvoiceType.Return &&
                            x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
                .SumAsync(x => (decimal?)x.Quantity) ?? decimal.Zero;

            remaining += Math.Max(decimal.Zero, originItem.Quantity - returned);
        }

        return decimal.Round(remaining, 3, MidpointRounding.ToEven);
    }

    /// <summary>Todas as NF-e de entrada da recusa foram canceladas pelo 2b: mesmo desfecho do "Cancelar recusa".</summary>
    private async Task CancelAsync(ShipmentLoadRefusal refusal, string userName)
    {
        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == refusal.ShipmentLoadKey);

        // Gravada como Cancelled ANTES do recálculo: ele lê a recusa pendente do banco.
        refusal.Status = ShipmentLoadRefusalStatus.Cancelled;
        refusal.CancelledAt = DateTime.Now;
        refusal.CancelledBy = userName;
        await db.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);

        movementLog.Register(
            load.Key,
            ShipmentLoadMovementType.RefusalCancelled,
            decimal.Zero,
            load.AvailableQuantity,
            "Recusa aguardando NF-e cancelada: as NF-e de entrada de todas as devoluções foram canceladas na SEFAZ.",
            userName);

        load.UpdatedAt = DateTime.Now;
        load.UpdatedBy = userName;
        await db.SaveChangesAsync();
    }
}
