using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Regras da perna de COMPRA (romaneio tipo 8) de uma Expedição de Grãos: ela só vive no contrato
/// da liberação dela, e a alocação não se mexe pela tela — a troca de contrato é a "Trocar
/// liberação" da carga.
/// </summary>
public static class ShippingPurchaseLegRules
{
    /// <summary>
    /// <c>null</c> quando o romaneio NÃO é perna de compra de Expedição; senão, o contrato da
    /// liberação dele (pode ser <see cref="Guid.Empty"/> se a liberação não for encontrada).
    /// </summary>
    public static async Task<Guid?> ReleaseContractKeyAsync(AppDbContext ctx, Guid storageTransactionKey)
    {
        var isShippingLeg = await ctx.ShippingTransactions.AsNoTracking()
            .AnyAsync(x => x.PurchaseStorageTransactionKey == storageTransactionKey);
        if (!isShippingLeg)
            return null;

        var contractKey = await (
                from t in ctx.StorageTransactions.AsNoTracking()
                where t.Key == storageTransactionKey
                join r in ctx.ShipmentReleases.AsNoTracking() on t.ShipmentReleaseKey equals r.Key
                select (Guid?)r.PurchaseContractKey)
            .FirstOrDefaultAsync();

        return contractKey ?? Guid.Empty;
    }
}
