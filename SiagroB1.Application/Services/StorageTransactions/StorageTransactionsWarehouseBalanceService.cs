using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageTransactions;

/// <summary>
/// Saldo do armazém para o produto, na mesma base das consultas por endereço.
/// </summary>
/// <remarks>
/// <b><c>Invoiced</c> conta junto com <c>Confirmed</c>, e isso é load-bearing.</b> Somando só
/// <c>Confirmed</c>, um romaneio de saída SUMIA da conta ao ser faturado e o volume que ele
/// havia retirado reaparecia como disponível — faturar não devolve grão ao armazém. O mesmo
/// valia na entrada: uma compra faturada deixava de contar como estoque e recusava embarque
/// legítimo.
/// <para>
/// <c>Cancelled</c> e <c>Returned</c> ficam de fora, e é assim que o retorno de documento de
/// saída re-credita o armazém de ORIGEM sozinho: o romaneio devolvido sai da soma. É o
/// mecanismo em que <c>SalesInvoicesReturnService</c> se apoia — ver o XML-doc dele sobre por
/// que o romaneio de origem nunca pode ficar <c>Returned</c> nos destinos novos.
/// </para>
/// <para>
/// A lista de status é a mesma de <c>StorageAddressesGetBalanceService</c> e das demais
/// consultas por endereço, que sempre contaram os dois. Esta era a única fora do padrão, e a
/// divergência fazia o mesmo armazém mostrar números diferentes conforme a tela.
/// </para>
/// <para>
/// Extraído de <c>StorageTransactionsConfirmedService</c> (era privado) para ser reaproveitado
/// por <c>SalesInvoicesReverseConfirmService</c>, que precisa da mesma fórmula para decidir se
/// uma devolução ao armazém ainda pode ser desfeita com segurança.
/// </para>
/// </remarks>
public static class StorageTransactionsWarehouseBalanceService
{
    public static async Task<decimal> CalculateAsync(
        AppDbContext context, string warehouseCode, string itemCode)
    {
        var total = await context.StorageTransactions
            .AsNoTracking()
            .Where(x => (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         x.TransactionStatus == StorageTransactionsStatus.Invoiced) &&
                        x.WarehouseCode == warehouseCode &&
                        x.ItemCode == itemCode &&
                        (x.TransactionType == StorageTransactionType.Purchase ||
                         x.TransactionType == StorageTransactionType.PurchaseReturn ||
                         x.TransactionType == StorageTransactionType.SalesShipment ||
                         x.TransactionType == StorageTransactionType.SalesShipmentReturn))
            .SumAsync(x => (x.TransactionType == StorageTransactionType.Purchase ||
                            x.TransactionType == StorageTransactionType.SalesShipmentReturn)
                ? x.NetWeight
                : -x.NetWeight);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }
}
