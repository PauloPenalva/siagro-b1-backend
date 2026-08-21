using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Escritor ÚNICO de <see cref="ShipmentLoad.InvoicedQuantity"/>, de
/// <see cref="ShipmentLoad.Status"/> (exceto <c>Cancelled</c>, que só o cancelamento grava) e
/// do <c>TransactionStatus</c> dos romaneios da carga.
/// </summary>
/// <remarks>
/// O saldo é persistido-derivado por SOMATÓRIO das notas, e não um ledger assinado: o eixo da
/// carga não tem movimento irreconstituível (realocação, conciliação cruzada, ajuste fiscal),
/// então um ledger seria uma segunda fonte de verdade do mesmo número — e a divergência entre
/// as duas seria invisível. Persistido, e não <c>[NotMapped]</c>, porque as duas telas filtram
/// e ordenam por saldo e status no servidor.
/// <para>
/// O par estático/instância é o mesmo de <c>SalesShipmentReleasesRecalculateShippedService</c>:
/// o estático calcula sem <c>SaveChanges</c>, para compor dentro de transação alheia.
/// </para>
/// <para>
/// <b>Sobre o TransactionStatus:</b> oscilar o romaneio entre <c>Confirmed</c> e
/// <c>Invoiced</c> é neutro em todos os saldos. Saldo de armazém, cobrança de armazenagem,
/// quebra técnica, saldo diário e fatura de serviço tratam os dois como equivalentes
/// (predicado <c>Confirmed || Invoiced</c>). E não dispara a armadilha "quem escreve
/// TransactionStatus direto precisa recalcular na mão":
/// <c>ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity</c> só conta
/// <c>Purchase</c>/<c>PurchaseReturn</c> e filtra por <c>!= Cancelled</c>.
/// </para>
/// </remarks>
public class ShipmentLoadsRecalculateInvoicedService(AppDbContext context)
{
    /// <summary>Tolerância de fechamento, a mesma casa decimal das quantidades.</summary>
    private const decimal Tolerance = 0.001m;

    public Task RecalculateAsync(Guid shipmentLoadKey) =>
        RecalculateAsync(context, shipmentLoadKey, excludedInvoiceKeys: null);

    public Task RecalculateAsync(Guid shipmentLoadKey, ICollection<Guid>? excludedInvoiceKeys) =>
        RecalculateAsync(context, shipmentLoadKey, excludedInvoiceKeys);

    /// <summary>
    /// Recalcula e ENFILEIRA as alterações no contexto, sem <c>SaveChanges</c> — quem chama
    /// decide quando salvar, para o saldo e o efeito que o mudou entrarem juntos.
    /// </summary>
    public static async Task RecalculateAsync(
        AppDbContext context,
        Guid shipmentLoadKey,
        ICollection<Guid>? excludedInvoiceKeys)
    {
        var load = await context.ShipmentLoads
            .FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);

        // Carga cancelada é estado terminal: não tem saldo e não projeta status nos romaneios,
        // que já foram devolvidos à Montagem. Espelha o contrato Finished.
        if (load == null || load.Status == ShipmentLoadStatus.Cancelled)
            return;

        var invoiced = await CalculateInvoicedAsync(context, shipmentLoadKey, excludedInvoiceKeys);

        load.InvoicedQuantity = invoiced;
        load.Status = ResolveStatus(load.TotalQuantity, invoiced);
        load.UpdatedAt = DateTime.Now;

        var shipmentStatus = load.Status == ShipmentLoadStatus.Invoiced
            ? StorageTransactionsStatus.Invoiced
            : StorageTransactionsStatus.Confirmed;

        var shipments = await context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .ToListAsync();

        foreach (var shipment in shipments)
        {
            // Cancelado e devolvido são estados DO ROMANEIO, não projeção da carga.
            if (shipment.TransactionStatus is StorageTransactionsStatus.Cancelled
                or StorageTransactionsStatus.Returned)
            {
                continue;
            }

            if (shipment.TransactionStatus == shipmentStatus)
                continue;

            shipment.TransactionStatus = shipmentStatus;
            shipment.UpdatedAt = DateTime.Now;
        }
    }

    public static ShipmentLoadStatus ResolveStatus(decimal totalQuantity, decimal invoicedQuantity) =>
        invoicedQuantity <= decimal.Zero ? ShipmentLoadStatus.Open
        : invoicedQuantity >= totalQuantity - Tolerance ? ShipmentLoadStatus.Invoiced
        : ShipmentLoadStatus.PartiallyInvoiced;

    /// <summary>
    /// A fórmula canônica:
    /// <code>
    /// Σ Items.Quantity  das notas  com ShipmentLoadKey = carga
    ///                              e InvoiceType = Normal
    ///                              e InvoiceStatus ∈ {Pending, Confirmed, Returned}
    ///   − Σ Items.Quantity  das notas  com InvoiceType = Return
    ///                                  e InvoiceStatus = Confirmed
    ///                                  e origem ∈ (notas normais da carga)
    /// </code>
    /// Cada cláusula tem um porquê:
    /// <list type="bullet">
    /// <item><b>Pending conta</b> — o consumo nasce na CRIAÇÃO da nota. É o que impede duas
    /// notas pendentes reservarem o mesmo volume. Consequência coerente: estornar a
    /// confirmação de uma nota normal NÃO devolve saldo; quem desfaz é cancelar ou excluir.</item>
    /// <item><b>Returned continua contando</b> — <c>SalesInvoicesReturnService</c> marca a
    /// origem como <c>Returned</c> já na criação do retorno, quando nada voltou fisicamente.
    /// O saldo volta quando a DEVOLUÇÃO é confirmada, que é onde o projeto considera que a
    /// devolução ocorreu.</item>
    /// <item><b>Cancelled não conta</b>, nos dois somatórios.</item>
    /// <item>A subtração é POR QUANTIDADE, então já funciona com devolução parcial.</item>
    /// </list>
    /// <paramref name="excludedInvoiceKeys"/> espelha o <c>excludedItemKeys</c> do lado do
    /// contrato e existe pelo mesmo motivo: <c>SumAsync</c> agrega NO SERVIDOR e leria o
    /// estado antigo quando a nota é removida na mesma transação.
    /// </summary>
    public static async Task<decimal> CalculateInvoicedAsync(
        AppDbContext context,
        Guid shipmentLoadKey,
        ICollection<Guid>? excludedInvoiceKeys)
    {
        // InvoiceStatus é anulável na entidade: a lista precisa ser do MESMO tipo, senão o
        // Contains não traduz para SQL.
        var liveStatuses = new InvoiceStatus?[] { InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned };

        var normalInvoices = context.SalesInvoices
            .Where(i => i.ShipmentLoadKey == shipmentLoadKey
                        && i.InvoiceType == SalesInvoiceType.Normal
                        && liveStatuses.Contains(i.InvoiceStatus));

        if (excludedInvoiceKeys is { Count: > 0 })
            normalInvoices = normalInvoices.Where(i => !excludedInvoiceKeys.Contains(i.Key));

        var normalKeys = await normalInvoices.Select(i => i.Key).ToListAsync();
        // SalesInvoiceItem.SalesInvoiceKey e SalesInvoiceOriginKey são Guid?: a lista de
        // comparação precisa do mesmo tipo para o Contains traduzir.
        var normalKeysNullable = normalKeys.Select(k => (Guid?)k).ToList();

        var billed = normalKeys.Count == 0
            ? decimal.Zero
            : await context.SalesInvoicesItems
                .Where(item => normalKeysNullable.Contains(item.SalesInvoiceKey))
                .SumAsync(item => (decimal?)item.Quantity) ?? decimal.Zero;

        // A origem da devolução precisa estar entre as notas normais VIVAS da carga: uma
        // devolução cuja origem foi cancelada não pode devolver saldo que já não é consumido.
        var returnInvoices = context.SalesInvoices
            .Where(i => i.InvoiceType == SalesInvoiceType.Return
                        && i.InvoiceStatus == InvoiceStatus.Confirmed
                        && i.SalesInvoiceOriginKey != null
                        && normalKeysNullable.Contains(i.SalesInvoiceOriginKey));

        if (excludedInvoiceKeys is { Count: > 0 })
            returnInvoices = returnInvoices.Where(i => !excludedInvoiceKeys.Contains(i.Key));

        var returnKeys = await returnInvoices.Select(i => i.Key).ToListAsync();
        var returnKeysNullable = returnKeys.Select(k => (Guid?)k).ToList();

        var returned = returnKeys.Count == 0
            ? decimal.Zero
            : await context.SalesInvoicesItems
                .Where(item => returnKeysNullable.Contains(item.SalesInvoiceKey))
                .SumAsync(item => (decimal?)item.Quantity) ?? decimal.Zero;

        return decimal.Round(billed - returned, 3, MidpointRounding.ToEven);
    }
}
