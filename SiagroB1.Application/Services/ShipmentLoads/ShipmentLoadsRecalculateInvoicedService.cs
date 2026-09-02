using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Escritor ÚNICO de <see cref="ShipmentLoad.InvoicedQuantity"/>, de
/// <see cref="ShipmentLoad.Status"/> e do <c>TransactionStatus</c> dos romaneios da carga.
/// </summary>
/// <remarks>
/// Duas exceções ao "escritor único" do <see cref="ShipmentLoad.Status"/>, ambas deliberadas:
/// <c>Cancelled</c>, que só o cancelamento grava, e <c>Planned</c> na criação, que é apenas o
/// mesmo valor que <see cref="ResolveStatus"/> devolveria para uma carga sem volume. A
/// vinculação e a desvinculação de romaneios NÃO escrevem status: elas alteram
/// <see cref="ShipmentLoad.TotalQuantity"/> e chamam este serviço.
/// <para>
/// O saldo é persistido-derivado por SOMATÓRIO das notas, e não um ledger assinado: o eixo da
/// carga não tem movimento irreconstituível (realocação, conciliação cruzada, ajuste fiscal),
/// então um ledger seria uma segunda fonte de verdade do mesmo número — e a divergência entre
/// as duas seria invisível. Persistido, e não <c>[NotMapped]</c>, porque as duas telas filtram
/// e ordenam por saldo e status no servidor.
/// </para>
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
public class ShipmentLoadsRecalculateInvoicedService(IUnitOfWork db)
{
    /// <summary>Tolerância de fechamento, a mesma casa decimal das quantidades.</summary>
    private const decimal Tolerance = 0.001m;

    public Task RecalculateAsync(Guid shipmentLoadKey) =>
        RecalculateAsync(db.Context, shipmentLoadKey, excludedInvoiceKeys: null);

    public Task RecalculateAsync(Guid shipmentLoadKey, ICollection<Guid>? excludedInvoiceKeys) =>
        RecalculateAsync(db.Context, shipmentLoadKey, excludedInvoiceKeys);

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

        // O terceiro termo é gravado AQUI, e não por um serviço próprio: o status depende dele,
        // e este é o escritor único do status. Ver ShipmentLoadsRecalculateReturnedService.
        var returned = await ShipmentLoadsRecalculateReturnedService
            .CalculateReturnedToWarehouseAsync(context, shipmentLoadKey);

        load.InvoicedQuantity = invoiced;
        load.ReturnedToWarehouseQuantity = returned;
        load.Status = ResolveStatus(load.TotalQuantity, invoiced, returned);
        load.UpdatedAt = DateTime.Now;

        // Carga ENCERRADA (faturada ou devolvida ao armazém) não devolve romaneio para
        // Confirmed, que é o filtro da tela de Montagem: a mercadoria já saiu, por venda ou por
        // devolução, e o romaneio não pode reaparecer como disponível para outra carga.
        var shipmentStatus = load.Status is ShipmentLoadStatus.Invoiced or ShipmentLoadStatus.Returned
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

    /// <summary>
    /// Resolve a situação a partir do volume montado e do saldo faturado.
    /// </summary>
    /// <remarks>
    /// <b>O primeiro ramo é o que separa planejamento de carga real</b>, e ele existe por um
    /// motivo concreto: sem ele, uma carga recém-criada pela Logística (<c>TotalQuantity</c> e
    /// <c>InvoicedQuantity</c> zerados) casaria o ramo <c>invoiced &lt;= 0</c> e viraria
    /// <c>Open</c> — passando a aparecer na tela de Faturamento de Expedição com saldo zero, em
    /// silêncio. O caminho mais curto para reproduzir isso era abrir a carga planejada e clicar
    /// em "Recalcular Saldo".
    /// <para>
    /// A decisão é por <see cref="ShipmentLoad.TotalQuantity"/> e NÃO pela contagem de
    /// romaneios: o que caracteriza um planejamento é não haver volume. Uma carga com volume e
    /// sem romaneio (possível só em teste) é <c>Open</c>, porque 90 toneladas não são um
    /// planejamento; e um romaneio de peso bruto zero deixa a carga em <c>Planned</c> sem
    /// consequência, já que não há o que faturar.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>O consumo é a soma dos dois abatimentos</b>, comercial e físico: uma carga com 25 t
    /// faturadas e 15 t devolvidas ao armazém, de 40 t montadas, está encerrada — e sem somar os
    /// dois ela leria "Faturada Parcial" com saldo zero, oferecendo-se ao Faturamento de
    /// Expedição para sempre.
    /// <para>
    /// Havendo devolução ao armazém, o encerramento é <c>Returned</c> e não <c>Invoiced</c>:
    /// parte da mercadoria não foi vendida, voltou. O rótulo prevalece porque esconder o retorno
    /// físico na lista é pior do que a carga mista aparecer como "Devolvida" — a tela de
    /// Detalhe mostra as três quantidades lado a lado.
    /// </para>
    /// </remarks>
    public static ShipmentLoadStatus ResolveStatus(
        decimal totalQuantity,
        decimal invoicedQuantity,
        decimal returnedToWarehouseQuantity)
    {
        if (totalQuantity <= Tolerance)
            return ShipmentLoadStatus.Planned;

        var consumed = invoicedQuantity + returnedToWarehouseQuantity;

        if (consumed <= decimal.Zero)
            return ShipmentLoadStatus.Open;

        if (consumed < totalQuantity - Tolerance)
            return ShipmentLoadStatus.PartiallyInvoiced;

        return returnedToWarehouseQuantity > Tolerance
            ? ShipmentLoadStatus.Returned
            : ShipmentLoadStatus.Invoiced;
    }

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
