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

        load.TotalQuantity = await SumAsync(context, load);
        load.UpdatedAt = DateTime.Now;
    }

    /// <remarks>
    /// Filtro de TIPO além da FK: o total é o volume que a carga MOVEU, e só o romaneio próprio
    /// da natureza dela conta — embarque na Normal, recebimento na Remoção (GAC-1175). A
    /// vinculação já recusa qualquer outro tipo, mas o filtro é a garantia de que uma transação
    /// que venha a apontar a carga por outro caminho (uma devolução, por exemplo) não infle o
    /// total em silêncio.
    /// </remarks>
    private static async Task<decimal> SumAsync(AppDbContext context, ShipmentLoad load)
    {
        var expectedType = ShipmentLoadsAttachTransactionsService.ExpectedTransactionType(load.LoadType);

        var shipments = await context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == load.Key && x.TransactionType == expectedType)
            .ToListAsync();

        return decimal.Round(shipments.Sum(x => x.GrossWeight), 3, MidpointRounding.ToEven);
    }
}
