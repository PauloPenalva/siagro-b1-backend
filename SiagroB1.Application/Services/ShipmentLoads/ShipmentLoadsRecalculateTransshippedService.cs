using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Fórmula de <see cref="ShipmentLoad.TransshippedQuantity"/> (GAC-1181) — o volume que saiu da
/// carga para um armazém intermediário.
/// </summary>
/// <remarks>
/// Só a fórmula, sem <c>SaveChanges</c> e sem estado, como
/// <see cref="ShipmentLoadsRecalculateReturnedService"/>. <b>Quem GRAVA é
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/></b>, o escritor único do status.
/// <para>
/// O estorno do transbordo APAGA a linha, então não há status a filtrar aqui: linha existente é
/// transbordo vivo.
/// </para>
/// </remarks>
public static class ShipmentLoadsRecalculateTransshippedService
{
    public static async Task<decimal> CalculateTransshippedAsync(AppDbContext context, Guid shipmentLoadKey)
    {
        var total = await context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .SumAsync(x => (decimal?)x.OutgoingQuantity) ?? decimal.Zero;

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// O ÚLTIMO transbordo ainda não tem saída vinculada — a mercadoria está no armazém
    /// intermediário e a viagem não acabou.
    /// </summary>
    public static async Task<bool> HasOpenTransshipmentAsync(AppDbContext context, Guid shipmentLoadKey)
    {
        var last = await context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync();

        if (last?.Key is not { } transshipmentKey)
            return false;

        return !await IsClosedAsync(context, transshipmentKey);
    }

    /// <summary>
    /// Este transbordo ESPECÍFICO já tem a Expedição de venda (<see cref="StorageTransactionType
    /// .SalesShipment"/>, o 7) vinculada — quem fecha de verdade o transbordo, ao contrário da
    /// saída do lote (GAC-1181 fase 2, Task 4), que não conclui nada.
    /// </summary>
    /// <remarks>
    /// Parametrizado pela CHAVE do transbordo, não pelo "último da carga" como
    /// <see cref="HasOpenTransshipmentAsync"/> — usado por quem decide sobre uma linha específica
    /// já achada por chave (<c>ShipmentLoadsTransshipmentAttachLotExitService</c>), onde presumir
    /// que ela é a última da carga reabriria um transbordo ANTIGO já concluído.
    /// </remarks>
    public static Task<bool> IsClosedAsync(AppDbContext context, Guid transshipmentKey) =>
        context.StorageTransactions.AnyAsync(x =>
            x.ShipmentLoadTransshipmentKey == transshipmentKey &&
            x.TransactionType == StorageTransactionType.SalesShipment &&
            x.TransactionStatus != StorageTransactionsStatus.Cancelled);
}
