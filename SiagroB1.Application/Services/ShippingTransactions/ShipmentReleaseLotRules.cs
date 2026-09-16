using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShippingTransactions;

/// <summary>
/// Lote de onde a mercadoria vai sair, quando a liberação aponta para um. Fonte única usada pela
/// Expedição de Grãos e pela troca de liberação (GAC-1177).
/// </summary>
/// <remarks>
/// Lote de onde a mercadoria vai sair, quando a liberação aponta para um.
/// Só as liberações emitidas por transferência de titularidade têm lote: nelas o
/// grão já está fisicamente depositado, e a saída precisa drenar aquele lote —
/// senão o Receipt(0) gravado pela transferência vira saldo fantasma permanente.
/// Liberação comum devolve null e o fluxo segue em nível de armazém, como sempre.
/// <para>
/// ⚠️ A exigência de lote é <see cref="ReleaseOriginRules.RequiresStorageAddress"/>, que NÃO
/// é o mesmo predicado do ramo de embarque acima. A liberação de
/// <see cref="ReleaseOrigin.SalesReturn"/> embarca sem perna de compra <b>e</b> sem lote: a
/// devolução ao armazém nasce sem <c>StorageAddressCode</c> — não é a regra geral de todo
/// romaneio tipo 12. O saldo de lote credita o 12 COM lote, que só o estorno da troca de
/// liberação (GAC-1177) grava. Generalizar este guard para <c>!= Standard</c> faria todo
/// reembarque de devolução estourar "liberação de transferência sem lote".
/// </para>
/// <para>
/// <paramref name="requiredQuantity"/> é o volume que precisa CABER no saldo do lote. Na criação é o
/// peso bruto do embarque; na troca é descontado o peso dos romaneios do mesmo lote que estão
/// saindo dele na mesma operação — senão inverter duas expedições do mesmo lote falharia por
/// saldo que a própria troca devolve.
/// </para>
/// </remarks>
public static class ShipmentReleaseLotRules
{
    public static async Task<StorageAddress?> ResolveAsync(
        AppDbContext context,
        IStorageAddressBalanceReader balanceReader,
        ShipmentRelease? release,
        string itemCode,
        string warehouseCode,
        decimal requiredQuantity)
    {
        if (release == null)
            return null;

        if (string.IsNullOrEmpty(release.StorageAddressCode))
        {
            // Integridade: uma liberação de transferência sem lote não tem como ser
            // embarcada corretamente. Só acontece com linha editada à mão.
            if (ReleaseOriginRules.RequiresStorageAddress(release.Origin))
                throw new ApplicationException(
                    "Liberação de transferência de propriedade sem lote de armazenagem vinculado.");

            return null;
        }

        var lot = await context.StorageAddresses
                      .AsNoTracking()
                      .FirstOrDefaultAsync(x => x.Code == release.StorageAddressCode)
                  ?? throw new ApplicationException(
                      $"Lote de armazenagem {release.StorageAddressCode} não encontrado.");

        if (!string.Equals(lot.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O produto do lote ({lot.ItemCode}) é diferente do produto do embarque ({itemCode}).");

        if (balanceReader.GetBalance(lot.Code!) < requiredQuantity)
            throw new ApplicationException(
                $"Saldo insuficiente no lote {lot.Code}: o produto desta liberação já foi movimentado.");

        // O armazém vem do payload da tela e é ele que o saldo de armazém usa. Divergindo
        // do armazém do lote, a saída debitaria um armazém e a entrada gravada pela
        // transferência ficaria presa no outro — dois saldos errados de uma vez.
        if (!string.Equals(lot.WarehouseCode, warehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O armazém do embarque ({warehouseCode}) é diferente do armazém do lote {lot.Code} ({lot.WarehouseCode}).");

        return lot;
    }
}
