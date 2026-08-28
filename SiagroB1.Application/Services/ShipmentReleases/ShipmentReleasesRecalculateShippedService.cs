using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesRecalculateShippedService(AppDbContext context)
{
    /// <summary>
    /// Tipos de romaneio que podem consumir o saldo de uma liberação. Fonte única da
    /// regra — os hooks em StorageTransactions consultam este predicado em vez de repetir
    /// a lista (a duplicação anterior deixou serviço e hooks divergirem em silêncio).
    /// <para>
    /// É um SUPERCONJUNTO de propósito: quais desses tipos realmente somam depende da
    /// origem da liberação (ver <see cref="CalculateShippedAsync"/>), e os hooks só têm a
    /// <c>ShipmentReleaseKey</c> em mãos — descobrir a origem custaria uma query por hook.
    /// Disparar um recálculo a mais é idempotente; deixar de disparar perde o consumo.
    /// </para>
    /// </summary>
    public static bool AffectsShippedQuantity(StorageTransactionType type) =>
        type is StorageTransactionType.Purchase
            or StorageTransactionType.PurchaseReturn
            or StorageTransactionType.SalesShipment
            or StorageTransactionType.SalesShipmentReturn;

    /// <summary>
    /// Calcula o volume romaneado SEM persistir nada, para quem precisa decidir
    /// antes de gravar (ex.: cancelamento, que recusa quando não há saldo).
    /// <para>
    /// A fórmula depende da <paramref name="origin"/> porque o romaneio que representa o
    /// movimento comercial da liberação muda:
    /// </para>
    /// <list type="bullet">
    /// <item><c>Standard</c> — a Expedição registra a COMPRA ao embarcar, então quem
    /// consome é <c>Purchase(8) − PurchaseReturn(9)</c>.</item>
    /// <item><c>OwnershipTransfer</c> — a compra já foi registrada e alocada no confirm da
    /// transferência; a Expedição só dá a SAÍDA, então quem consome é
    /// <c>SalesShipment(7) − SalesShipmentReturn(12)</c>.</item>
    /// </list>
    /// A origem é parâmetro (e não uma leitura interna) para que o compilador aponte todo
    /// chamador ao mudar a regra — todos já têm a entidade carregada.
    /// </summary>
    public async Task<decimal> CalculateShippedAsync(Guid shipmentReleaseKey, ReleaseOrigin origin)
    {
        // Pending conta, Cancelled não. Listas inline (e não AffectsShippedQuantity)
        // porque o EF precisa traduzir para SQL.
        var query = context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey == shipmentReleaseKey
                        && t.TransactionStatus != StorageTransactionsStatus.Cancelled);

        if (origin == ReleaseOrigin.OwnershipTransfer)
        {
            return await query
                .Where(t => t.TransactionType == StorageTransactionType.SalesShipment
                            || t.TransactionType == StorageTransactionType.SalesShipmentReturn)
                .SumAsync(t => t.TransactionType == StorageTransactionType.SalesShipment
                    ? t.NetWeight
                    : -t.NetWeight);
        }

        return await query
            .Where(t => t.TransactionType == StorageTransactionType.Purchase
                        || t.TransactionType == StorageTransactionType.PurchaseReturn)
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

        release.ShippedQuantity = await CalculateShippedAsync(shipmentReleaseKey, release.Origin);

        await context.SaveChangesAsync();
    }
}
