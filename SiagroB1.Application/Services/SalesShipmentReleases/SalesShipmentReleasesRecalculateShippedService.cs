using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesRecalculateShippedService(AppDbContext context)
{
    /// <summary>
    /// Tipos de romaneio que consomem o saldo da liberação de VENDA. Fonte única da regra —
    /// os hooks do faturamento consultam este predicado em vez de repetir a lista.
    /// (Devolução de venda NÃO cria romaneio novo; vira o status da <c>SalesShipment</c>
    /// original para <c>Returned</c>, que sai da soma — restaurando o saldo.)
    /// </summary>
    public static bool AffectsShippedQuantity(StorageTransactionType type) =>
        type is StorageTransactionType.SalesShipment;

    /// <summary>
    /// Calcula o volume romaneado/faturado SEM persistir nada, para quem precisa decidir
    /// antes de gravar (ex.: cancelamento, que recusa quando não há saldo).
    /// Conta apenas romaneios de venda efetivamente faturados (<c>Invoiced</c>); cancelados
    /// (voltam a <c>Confirmed</c> e perdem a chave) e devolvidos (<c>Returned</c>) ficam de fora.
    /// </summary>
    public async Task<decimal> CalculateShippedAsync(Guid salesShipmentReleaseKey)
    {
        return await context.StorageTransactions
            .Where(t => t.SalesShipmentReleaseKey == salesShipmentReleaseKey
                        && t.TransactionType == StorageTransactionType.SalesShipment
                        && t.TransactionStatus == StorageTransactionsStatus.Invoiced)
            .SumAsync(t => t.NetWeight);
    }

    public async Task RecalculateAsync(Guid salesShipmentReleaseKey)
    {
        var release = await context.SalesShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == salesShipmentReleaseKey);

        if (release is null)
            return;

        release.ShippedQuantity = await CalculateShippedAsync(salesShipmentReleaseKey);

        await context.SaveChangesAsync();
    }
}
