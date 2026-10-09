using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Devolução criada por uma recusa de carga aguardando NF-e (spec 2026-10-09 §5.4) não é cancelada nem excluída
/// avulsa: a recusa ficaria pendente com uma devolução a menos e a carga travada sem explicação.
/// </summary>
public static class SalesInvoicesRefusalLink
{
    public static async Task EnsureNotInPendingRefusalAsync(AppDbContext context, SalesInvoice invoice)
    {
        if (invoice.ShipmentLoadRefusalKey is not { } refusalKey)
            return;

        var loadCode = await context.ShipmentLoadRefusals.AsNoTracking()
            .Where(r => r.Key == refusalKey && r.Status == ShipmentLoadRefusalStatus.Pending)
            .Select(r => r.ShipmentLoad!.Code)
            .FirstOrDefaultAsync();

        if (loadCode is not null)
            throw new DefaultException(
                $"Esta devolução pertence à recusa da carga {loadCode}: cancele a recusa na Montagem de Carga.");
    }

    /// <summary>
    /// A devolução que concluiu a recusa já gerou os efeitos do destino (entrada no armazém, liberações, transbordo):
    /// cancelar a NF-e dela pelo Siagro deixaria esses efeitos para trás. Barrada antes de falar com a SEFAZ.
    /// </summary>
    public static async Task EnsureNotInCompletedRefusalAsync(AppDbContext context, SalesInvoice invoice)
    {
        if (invoice.ShipmentLoadRefusalKey is not { } refusalKey)
            return;

        var loadCode = await context.ShipmentLoadRefusals.AsNoTracking()
            .Where(r => r.Key == refusalKey && r.Status == ShipmentLoadRefusalStatus.Completed)
            .Select(r => r.ShipmentLoad!.Code)
            .FirstOrDefaultAsync();

        if (loadCode is not null)
            throw new DefaultException(
                $"Esta devolução concluiu a recusa da carga {loadCode}: a NF-e não pode ser cancelada pelo Siagro.");
    }
}
