using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

public static class PurchaseTransactionChangeQuery
{
    /// <summary>
    /// Quantidade total já trocada (para outro fornecedor/armazém) de um romaneio de compra.
    /// Sai do saldo alocável do original — ver <c>StorageTransaction.RecalculateAvailableVolume</c>.
    /// </summary>
    public static async Task<decimal> ChangedQuantityAsync(AppDbContext ctx, Guid storageTransactionKey)
        => await ctx.PurchaseTransactionChanges
            .Where(x => x.OriginalStorageTransactionKey == storageTransactionKey)
            .SumAsync(x => (decimal?)x.Quantity) ?? decimal.Zero;
}
