using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// "Cancelar recusa" (spec 2026-10-09 §5.4): desfaz a recusa aguardando NF-e enquanto nenhuma NF-e de entrada dela
/// foi autorizada nem está em processamento. Cancela as devoluções vivas e destrava a carga.
/// </summary>
public class ShipmentLoadsCancelRefusalService(
    IUnitOfWork db,
    SalesInvoicesCancelService cancelService,
    ShipmentLoadsMovementLogService movementLog,
    ILogger<ShipmentLoadsCancelRefusalService> logger)
{
    public async Task<ShipmentLoad> ExecuteAsync(Guid shipmentLoadKey, string userName)
    {
        var load = await db.Context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {shipmentLoadKey}");

        var refusal = await db.Context.ShipmentLoadRefusals
                          .FirstOrDefaultAsync(x => x.ShipmentLoadKey == shipmentLoadKey &&
                                                    x.Status == ShipmentLoadRefusalStatus.Pending)
                      ?? throw new DefaultException($"A carga {load.Code} não tem recusa aguardando NF-e.");

        var returns = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.ShipmentLoadRefusalKey == refusal.Key && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .ToListAsync();

        // Autorizada já é documento fiscal; em processamento pode virar um a qualquer momento.
        var blocking = returns.FirstOrDefault(i => i.NfeStatus is NfeStatus.Authorized or NfeStatus.Processing);
        if (blocking is not null)
            throw new DefaultException(
                $"A NF-e de entrada do documento {blocking.InvoiceNumber} já foi autorizada ou está em processamento: " +
                "cancele a NF-e ou aguarde o retorno antes de cancelar a recusa.");

        try
        {
            await db.BeginTransactionAsync();

            // O gancho de saldo de cada cancelamento recalcula a carga com a recusa ainda Pending (segue travada).
            foreach (var returnInvoice in returns)
                await cancelService.CancelForRefusalAsync(returnInvoice.Key, userName);

            // A recusa precisa estar gravada como Cancelled ANTES do recálculo final: ele lê a recusa pendente do banco.
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
                $"Recusa aguardando NF-e cancelada: {returns.Count} devolução(ões) cancelada(s) " +
                $"({string.Join(", ", returns.Select(i => i.InvoiceNumber))}).",
                userName);

            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao cancelar a recusa da carga {Code}", load.Code);
            throw;
        }

        return load;
    }
}
