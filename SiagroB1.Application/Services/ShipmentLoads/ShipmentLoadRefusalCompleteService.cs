using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Segundo tempo da recusa de carga na filial que emite NF-e pelo Siagro (spec 2026-10-09 §5.3). Chamado por
/// <c>SalesInvoicesConfirmService</c> a cada devolução confirmada; quando TODAS as devoluções vivas da recusa estão
/// confirmadas, aplica os efeitos do destino e conclui a recusa — na transação da confirmação.
/// </summary>
/// <remarks>
/// Falha aqui derruba a confirmação inteira; <c>NfeResultHandlerBase</c> mantém a NF-e Autorizada, grava o erro e o
/// "Concluir confirmação" refaz tudo. A quantidade é a soma das devoluções CONFIRMADAS: uma devolução cuja NF-e foi
/// cancelada pelo 2b sai da conta (Review Focus 3).
/// </remarks>
public class ShipmentLoadRefusalCompleteService(IUnitOfWork db, ShipmentLoadRefusalEffectsService effects)
{
    public async Task ApplyAsync(SalesInvoice confirmedReturn, string userName)
    {
        if (confirmedReturn.ShipmentLoadRefusalKey is not { } refusalKey)
            return;

        var refusal = await db.Context.ShipmentLoadRefusals.FirstOrDefaultAsync(x => x.Key == refusalKey);

        if (refusal is not { Status: ShipmentLoadRefusalStatus.Pending })
            return;

        var returns = await db.Context.SalesInvoices
            .Include(i => i.Items)
            .Where(i => i.ShipmentLoadRefusalKey == refusalKey && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .ToListAsync();

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
            await effects.ApplyAsync(
                new RefusalEffectsInput(
                    load, refusal.Destination, refusal.DestinationWarehouseCode, refusal.DestinationWarehouseName,
                    origins, totalQuantity, refusal.Reason),
                userName);
        }

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
        load.UpdatedAt = DateTime.Now;
        load.UpdatedBy = userName;
        await db.SaveChangesAsync();
    }
}
