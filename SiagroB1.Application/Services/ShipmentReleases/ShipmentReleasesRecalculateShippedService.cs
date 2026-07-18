using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesRecalculateShippedService(AppDbContext context)
{
    /// <summary>
    /// Tipos de romaneio que consomem o saldo da liberação. Fonte única da regra —
    /// os hooks em StorageTransactions consultam este predicado em vez de repetir
    /// a lista (a duplicação anterior deixou serviço e hooks divergirem em silêncio).
    /// </summary>
    public static bool AffectsShippedQuantity(StorageTransactionType type) =>
        type is StorageTransactionType.Purchase or StorageTransactionType.PurchaseReturn;

    /// <summary>
    /// Calcula o volume romaneado SEM persistir nada, para quem precisa decidir
    /// antes de gravar (ex.: cancelamento, que recusa quando não há saldo).
    /// </summary>
    public async Task<decimal> CalculateShippedAsync(Guid shipmentReleaseKey)
    {
        // usado = Σ(Purchase.Net) − Σ(PurchaseReturn.Net); Pending conta, Cancelled não.
        // Lista inline (e não AffectsShippedQuantity) porque o EF precisa traduzir para SQL.
        return await context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey == shipmentReleaseKey
                        && t.TransactionStatus != StorageTransactionsStatus.Cancelled
                        && (t.TransactionType == StorageTransactionType.Purchase
                            || t.TransactionType == StorageTransactionType.PurchaseReturn))
            .SumAsync(t => t.TransactionType == StorageTransactionType.Purchase
                ? t.NetWeight
                : -t.NetWeight);
    }

    public async Task RecalculateAsync(Guid shipmentReleaseKey)
    {
        var release = await context.ShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == shipmentReleaseKey);

        if (release is null)
            return;

        release.ShippedQuantity = await CalculateShippedAsync(shipmentReleaseKey);

        await context.SaveChangesAsync();
    }
}
