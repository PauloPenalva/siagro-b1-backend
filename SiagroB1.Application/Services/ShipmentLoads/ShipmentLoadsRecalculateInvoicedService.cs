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
/// <para>
/// GAC-1171 (melhorias): o ramo Faturada se desdobra em Faturada, Descarregada e Concluída por
/// <see cref="ResolveClosure"/>. A marca <c>IsDischarged</c> é escrita pelos serviços
/// Marcar/Desfazer, mas o status continua saindo daqui.
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

        // Carga de REMOÇÃO (GAC-1175) não fatura: não há nota para somar, e o ciclo dela é
        // Planned → Open → Completed. Sai antes da projeção de status nos romaneios porque os
        // romaneios dela pertencem à ENTRADA, não à carga — carimbá-los como Invoiced faria a
        // entrada parecer faturada e bloquearia o estorno dela.
        if (load.LoadType == ShipmentLoadType.Removal)
        {
            ResolveRemoval(load);
            return;
        }

        var invoiced = await CalculateInvoicedAsync(context, shipmentLoadKey, excludedInvoiceKeys);

        // O terceiro termo é gravado AQUI, e não por um serviço próprio: o status depende dele,
        // e este é o escritor único do status. Ver ShipmentLoadsRecalculateReturnedService.
        var returned = await ShipmentLoadsRecalculateReturnedService
            .CalculateReturnedToWarehouseAsync(context, shipmentLoadKey);

        // O quarto termo (GAC-1181), mesmo motivo do terceiro: o status depende dele.
        var transshipped = await ShipmentLoadsRecalculateTransshippedService
            .CalculateTransshippedAsync(context, shipmentLoadKey);

        var hasOpenTransshipment = await ShipmentLoadsRecalculateTransshippedService
            .HasOpenTransshipmentAsync(context, shipmentLoadKey);

        var baseStatus = ResolveStatus(load.TotalQuantity, invoiced, returned, transshipped, hasOpenTransshipment);

        // GAC-1171 (melhorias): a marca manual só vale para a mercadoria que estava faturada
        // quando o usuário a marcou. Uma nota cancelada, excluída ou devolvida (ou um transbordo)
        // tira a carga de Faturada, e um faturamento novo depois disso não pode herdar em
        // silêncio um "Descarregada" que se referia a outra mercadoria.
        if (baseStatus != ShipmentLoadStatus.Invoiced)
            load.IsDischarged = false;

        // Só o ramo Faturada usa a Conferência: fora dele a consulta seria trabalho à toa.
        var allDeliveriesClosed = baseStatus == ShipmentLoadStatus.Invoiced
            && await AreAllDeliveriesClosedAsync(context, shipmentLoadKey, excludedInvoiceKeys);

        load.InvoicedQuantity = invoiced;
        load.ReturnedToWarehouseQuantity = returned;
        load.TransshippedQuantity = transshipped;
        load.Status = ResolveClosure(baseStatus, load.IsDischarged, allDeliveriesClosed);
        load.UpdatedAt = DateTime.Now;

        // Carga ENCERRADA (faturada, descarregada, concluída ou devolvida ao armazém) não
        // devolve romaneio para Confirmed, que é o filtro da tela de Montagem: a mercadoria já
        // saiu, por venda ou por devolução, e o romaneio não pode reaparecer como disponível
        // para outra carga. Sem Discharged/Completed aqui, marcar a carga como descarregada
        // devolveria os romaneios à Montagem.
        var shipmentStatus = load.Status is ShipmentLoadStatus.Invoiced or ShipmentLoadStatus.Returned
            or ShipmentLoadStatus.Discharged or ShipmentLoadStatus.Completed
            ? StorageTransactionsStatus.Invoiced
            : StorageTransactionsStatus.Confirmed;

        // O romaneio de ENTRADA do transbordo (TransshipmentReceipt) NÃO carrega ShipmentLoadKey
        // — por desenho: o cancelamento da carga zera essa chave, e ele deixaria a entrada órfã
        // se dependesse dela. Ele já está fora desta consulta por isso, sem precisar de filtro de
        // tipo: só o romaneio de SAÍDA (embarque) da carga aponta ShipmentLoadKey.
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
    /// Situação da carga de remoção: só volume, sem saldo.
    /// </summary>
    /// <remarks>
    /// <c>Completed</c> é terminal e MANUAL — o recálculo não o reescreve, pelo mesmo motivo de
    /// <c>Cancelled</c>: vincular ou desvincular já são recusados nesse estado, e qualquer outra
    /// chamada ao recálculo (o botão "Recalcular Saldo", por exemplo) reabriria a carga em
    /// silêncio. Quem desfaz a conclusão é <c>ShipmentLoadsReopenService</c>.
    /// </remarks>
    private static void ResolveRemoval(ShipmentLoad load)
    {
        load.InvoicedQuantity = decimal.Zero;
        load.ReturnedToWarehouseQuantity = decimal.Zero;
        load.UpdatedAt = DateTime.Now;

        if (load.Status == ShipmentLoadStatus.Completed)
            return;

        load.Status = load.TotalQuantity <= Tolerance
            ? ShipmentLoadStatus.Planned
            : ShipmentLoadStatus.Open;
    }

    /// <summary>
    /// Resolve a situação a partir do volume montado e do saldo faturado.
    /// </summary>
    /// <remarks>
    /// <b>O ramo do transbordo vem PRIMEIRO</b> (GAC-1181), antes até do <c>Planned</c>. Depois
    /// que o volume sai da carga para o armazém intermediário o saldo é zero, e sem este ramo a
    /// carga leria "Faturada" (ou "Planejada", se o resto também for zero) e sumiria das telas de
    /// pendência com a mercadoria ainda no meio do caminho — a mesma armadilha que o ramo
    /// <c>Planned</c> corrigiu quando <c>ResolveStatus(0, 0)</c> devolvia <c>Open</c>.
    /// </remarks>
    /// <remarks>
    /// <b>O segundo ramo é o que separa planejamento de carga real</b>, e ele existe por um
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
    /// <b>O encerramento é decidido pelo SALDO (os quatro termos), o rótulo só pelo consumo
    /// COMERCIAL (faturado + devolvido ao armazém)</b> — o transbordo fecha a carga, mas não é
    /// venda nem devolução, então não pode fazer a carga ler "Faturada Parcial" sozinho: depois
    /// que a saída do transbordo é vinculada e nada foi faturado ainda, <c>available</c> fica
    /// positivo (o comercial não tocou a carga) e o resultado é <c>Open</c>, não
    /// <c>PartiallyInvoiced</c> — o rótulo mentia dizendo "parcialmente faturada" quando o
    /// faturado era zero.
    /// <para>
    /// Uma carga com 25 t faturadas e 15 t devolvidas ao armazém, de 40 t montadas, está
    /// encerrada — e sem somar os dois abatimentos comerciais ela leria "Faturada Parcial" com
    /// saldo zero, oferecendo-se ao Faturamento de Expedição para sempre.
    /// </para>
    /// <para>
    /// Havendo devolução ao armazém, o encerramento é <c>Returned</c> e não <c>Invoiced</c>:
    /// parte da mercadoria não foi vendida, voltou. O rótulo prevalece porque esconder o retorno
    /// físico na lista é pior do que a carga mista aparecer como "Devolvida" — a tela de
    /// Detalhe mostra as quatro quantidades lado a lado.
    /// </para>
    /// <para>
    /// Sem transbordo (<c>transshippedQuantity == 0</c>) este método se comporta exatamente como
    /// antes: <c>available &lt;= Tolerance</c> é equivalente a
    /// <c>invoiced + returned &gt;= total - Tolerance</c>, o critério antigo de fechamento, e as
    /// duas ramificações intermediárias nunca se sobrepõem porque o ramo <c>Planned</c> já
    /// garantiu <c>total &gt; Tolerance</c> antes de chegar aqui.
    /// </para>
    /// </remarks>
    public static ShipmentLoadStatus ResolveStatus(
        decimal totalQuantity,
        decimal invoicedQuantity,
        decimal returnedToWarehouseQuantity,
        decimal transshippedQuantity,
        bool hasOpenTransshipment)
    {
        if (hasOpenTransshipment)
            return ShipmentLoadStatus.InTransshipment;

        if (totalQuantity <= Tolerance)
            return ShipmentLoadStatus.Planned;

        var available = totalQuantity
            - invoicedQuantity
            - returnedToWarehouseQuantity
            - transshippedQuantity;

        if (available <= Tolerance)
        {
            return returnedToWarehouseQuantity > Tolerance
                ? ShipmentLoadStatus.Returned
                : ShipmentLoadStatus.Invoiced;
        }

        if (invoicedQuantity + returnedToWarehouseQuantity <= decimal.Zero)
            return ShipmentLoadStatus.Open;

        return ShipmentLoadStatus.PartiallyInvoiced;
    }

    /// <summary>
    /// Desdobra o <c>Invoiced</c> de <see cref="ResolveStatus"/> pela marca manual e pela
    /// Conferência de Entregas (GAC-1171, melhorias). Qualquer outro status passa intacto.
    /// </summary>
    /// <remarks>
    /// A Concluída vence a marca: ela é a afirmação mais forte ("tudo foi conferido"), e passar
    /// por Descarregada antes é opcional. É por isso que desfazer a descarga com a carga
    /// Concluída é recusado (<c>ShipmentLoadsUndoDischargedService</c>): o botão não teria
    /// efeito visível.
    /// </remarks>
    public static ShipmentLoadStatus ResolveClosure(
        ShipmentLoadStatus baseStatus,
        bool isDischarged,
        bool allDeliveriesClosed)
    {
        if (baseStatus != ShipmentLoadStatus.Invoiced)
            return baseStatus;

        if (allDeliveriesClosed)
            return ShipmentLoadStatus.Completed;

        return isDischarged ? ShipmentLoadStatus.Discharged : ShipmentLoadStatus.Invoiced;
    }

    /// <summary>
    /// Verdadeiro quando a Conferência de Entregas da carga está toda encerrada: há ao menos um
    /// item em nota Normal Confirmada, nenhuma nota Normal Pendente, e todos os itens das
    /// Confirmadas estão <c>Closed</c>. Canceladas e Retornadas ficam fora, como ficam fora da
    /// tela de Conferência.
    /// </summary>
    /// <remarks>
    /// ⚠️ Materializa as notas com os itens em vez de agregar no servidor, e filtra o status EM
    /// MEMÓRIA. O motivo é o mesmo do <c>excludedInvoiceKeys</c> de
    /// <see cref="CalculateInvoicedAsync"/>: o recálculo roda dentro de transações alheias, às
    /// vezes antes do flush. O cancelamento de uma devolução, por exemplo, reabre a nota de
    /// origem e chama o hook da carga sem salvar antes. Uma consulta com o status no WHERE leria
    /// o banco, veria a origem ainda Retornada e concluiria a carga com um item reaberto. Com a
    /// consulta rastreada, o EF devolve as instâncias já rastreadas com os valores atuais, e o
    /// filtro em memória enxerga a mudança. São poucas notas por carga, então o custo é
    /// irrelevante.
    /// </remarks>
    public static async Task<bool> AreAllDeliveriesClosedAsync(
        AppDbContext context,
        Guid shipmentLoadKey,
        ICollection<Guid>? excludedInvoiceKeys)
    {
        var invoices = await context.SalesInvoices
            .Include(i => i.Items)
            .Where(i => i.ShipmentLoadKey == shipmentLoadKey
                        && i.InvoiceType == SalesInvoiceType.Normal)
            .ToListAsync();

        var live = invoices
            .Where(i => context.Entry(i).State != EntityState.Deleted)
            .Where(i => excludedInvoiceKeys is not { Count: > 0 } || !excludedInvoiceKeys.Contains(i.Key))
            .ToList();

        if (live.Any(i => i.InvoiceStatus == InvoiceStatus.Pending))
            return false;

        var items = live
            .Where(i => i.InvoiceStatus == InvoiceStatus.Confirmed)
            .SelectMany(i => i.Items)
            .Where(item => context.Entry(item).State != EntityState.Deleted)
            .ToList();

        return items.Count > 0
               && items.All(item => item.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed);
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
