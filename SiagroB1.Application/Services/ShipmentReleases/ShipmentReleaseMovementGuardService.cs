using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleaseMovementGuardService(AppDbContext context)
{
    /// <summary>
    /// Rejeita romanear contra uma liberação não disponível: Completed (finalizada),
    /// Cancelled ou Paused. Cobre tanto os romaneios de venda (SalesShipment/
    /// SalesShipmentReturn) quanto os de compra (Purchase/PurchaseReturn) — sem isso,
    /// um lançamento novo poderia ir para o armazém de uma liberação já cancelada
    /// por troca de armazém.
    /// </summary>
    public async Task EnsureCanShipAsync(StorageTransaction transaction)
    {
        if (transaction.ShipmentReleaseKey is not { } releaseKey)
            return;

        if (transaction.TransactionType is not (StorageTransactionType.SalesShipment
            or StorageTransactionType.SalesShipmentReturn
            or StorageTransactionType.Purchase
            or StorageTransactionType.PurchaseReturn))
            return;

        var status = await context.ShipmentReleases
            .Where(r => r.Key == releaseKey)
            .Select(r => (ReleaseStatus?)r.Status)
            .FirstOrDefaultAsync();

        if (status is ReleaseStatus.Completed or ReleaseStatus.Cancelled or ReleaseStatus.Paused)
            throw new ApplicationException(
                "Liberação de embarque finalizada/cancelada/pausada: não é possível romanear.");
    }
}
