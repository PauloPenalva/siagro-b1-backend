using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Escritor ÚNICO de <see cref="ShipmentLoad.TotalQuantity"/> — a soma do <c>GrossWeight</c>
/// dos romaneios vinculados à carga.
/// </summary>
/// <remarks>
/// O campo já foi gravado num lugar só, na montagem, quando a carga nascia pronta. Agora que
/// romaneios entram e saem depois, ele precisa de escritor único pelo mesmo motivo que
/// <see cref="ShipmentLoad.InvoicedQuantity"/> tem o seu: dois lugares somando o mesmo número
/// divergem em silêncio, e a divergência não tem como ser percebida.
/// <para>
/// ⚠️ <b>CHAMAR SÓ DEPOIS DO <c>SaveChangesAsync</c> que gravou as FKs.</b> A busca dos
/// romaneios é uma consulta ao banco: as alterações de <c>ShipmentLoadKey</c> ainda pendentes
/// no change tracker são invisíveis para ela, e o total sairia com o conjunto ANTERIOR de
/// romaneios — errado, e sem nada indicando que está errado. A soma em si é feita em memória,
/// sobre a lista materializada, e não com <c>SumAsync</c>.
/// </para>
/// <para>
/// Não escreve <see cref="ShipmentLoad.Status"/>: quem faz isso é
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/>, chamado logo depois. Como o status
/// deriva do total, a ordem importa — total primeiro, status depois.
/// </para>
/// </remarks>
public static class ShipmentLoadsRecalculateTotalService
{
    /// <summary>
    /// Recalcula e ENFILEIRA a alteração no contexto, sem <c>SaveChanges</c>: quem chama decide
    /// quando salvar, para o total e o vínculo que o mudou entrarem juntos.
    /// </summary>
    public static async Task RecalculateAsync(AppDbContext context, Guid shipmentLoadKey)
    {
        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);

        if (load == null)
            return;

        // Filtro de TIPO além da FK: o total é o volume EMBARCADO, e só romaneio de embarque
        // conta. Hoje é redundante — ShipmentLoadsAttachTransactionsService só aceita
        // SalesShipment —, mas é a garantia de que qualquer transação de outro tipo que venha
        // a apontar a carga (uma devolução, por exemplo) não infle o total em silêncio.
        var shipments = await context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey &&
                        x.TransactionType == StorageTransactionType.SalesShipment)
            .ToListAsync();

        load.TotalQuantity = decimal.Round(
            shipments.Sum(x => x.GrossWeight), 3, MidpointRounding.ToEven);
        load.UpdatedAt = DateTime.Now;
    }
}
