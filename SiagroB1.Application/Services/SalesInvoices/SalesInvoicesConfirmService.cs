using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesConfirmService(
    IUnitOfWork db,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    SalesContractsAllocationCreateService allocationCreate,
    SalesContractsAllocationCreateForReturnService allocationCreateForReturn,
    SalesInvoicesUsageGuardService usageGuard,
    SalesContractsAllocationCreateForFiscalAdjustmentService fiscalAdjustment,
    ShipmentLoadsBalanceHookService loadHook,
    IStringLocalizer<Resource> resource)
{
    /// <summary>Tolerância de fechamento, a mesma casa decimal das quantidades.</summary>
    private const decimal Tolerance = 0.001m;

    /// <param name="commitMode">
    /// <c>Auto</c> abre e comita transação própria. <c>Deferred</c> compõe dentro da transação
    /// de quem chama e NÃO comita nem faz rollback.
    /// <para>
    /// ⚠️ <c>Deferred</c> não é conveniência: <c>UnitOfWork.CommitAsync</c> comita e zera
    /// <c>_transaction</c> INCONDICIONALMENTE. Chamado em <c>Auto</c> de dentro de uma
    /// transação alheia, este serviço comitaria a transação DO CHAMADOR no meio da operação —
    /// tudo o que viesse depois rodaria sem proteção e o commit final estouraria NRE.
    /// É o que <c>ShipmentLoadsRefuseService</c> precisa evitar ao confirmar N devoluções.
    /// </para>
    /// </param>
    /// <param name="shipmentOutcomes">
    /// Destino de cada romaneio da origem numa devolução do fluxo LEGADO: a chave do romaneio e o
    /// <see cref="StorageTransactionsStatus"/> em que ele deve ficar. Só os romaneios listados são
    /// tocados.
    /// <para>
    /// <c>null</c> mantém o comportamento histórico — TODOS os romaneios da origem viram
    /// <c>Returned</c> —, e é o que preserva as devoluções criadas antes desta feature, que ficaram
    /// pendentes e serão confirmadas pela tela.
    /// </para>
    /// <para>
    /// ⚠️ <b>Nenhum dos dois destinos do retorno deixa o romaneio em <c>Returned</c>.</b> Aquele
    /// status significa "o embarque não aconteceu": o romaneio sai das consultas de saldo e o
    /// armazém de ORIGEM é re-creditado sozinho. Nos dois destinos o grão saiu de lá — segue no
    /// caminhão (<c>Confirmed</c>, disponível para refaturar) ou foi descarregado em outro armazém
    /// (<c>Invoiced</c>, e o crédito vai para o romaneio de devolução). Deixá-lo <c>Returned</c>
    /// no destino armazém creditaria o grão duas vezes.
    /// </para>
    /// </param>
    public async Task ExecuteAsync(
        Guid key,
        string userName,
        CommitMode commitMode = CommitMode.Auto,
        IReadOnlyDictionary<Guid, StorageTransactionsStatus>? shipmentOutcomes = null)
    {
        var invoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException(
                resource["SALES_INVOICE_NOT_FOUND"].Value);

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
        {
            throw new ApplicationException(
                "Invoice is not pending.");
        }

        if (commitMode == CommitMode.Auto)
            await db.BeginTransactionAsync();

        try
        {
            var affectedReleaseKeys = new HashSet<Guid>();

            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                await ProcessReturnInvoiceAsync(
                    invoice,
                    userName,
                    affectedReleaseKeys,
                    shipmentOutcomes);

                // Ledger: linhas negativas proporcionais à distribuição vigente dos itens
                // de origem (respeita realocações). Devolve saldo às liberações onde o
                // volume está alocado hoje.
                var returnReleases = await allocationCreateForReturn.ExecuteAsync(
                    invoice, userName, CommitMode.Deferred);
                affectedReleaseKeys.UnionWith(returnReleases);
            }
            // Pelo RESOLVEDOR, não pela contagem de SalesTransactions: o documento de carga
            // tem essa coleção vazia e cairia no ramo AVULSO abaixo, gravando alocação de
            // ajuste fiscal em vez de faturamento — corrompendo o saldo do contrato em
            // silêncio, e só por Estornar Confirmação -> Confirmar.
            else if (SalesInvoiceOriginResolver.ConsumesShipments(invoice))
            {
                await ProcessNormalInvoiceAsync(
                    invoice,
                    userName);

                // Ledger: alocação padrão (item → contrato original, consumindo a liberação).
                await allocationCreate.ExecuteForInvoiceAsync(
                    invoice, userName, CommitMode.Deferred);

                foreach (var item in invoice.Items)
                {
                    if (item.SalesShipmentReleaseKey is { } itemReleaseKey)
                        affectedReleaseKeys.Add(itemReleaseKey);
                }
            }
            else
            {
                // Documento AVULSO (sem romaneio): quem decide o efeito no contrato é a
                // natureza de operação.
                //
                // O caminho é escolhido pela ORIGEM do documento — tem romaneio ou não — e
                // não pela natureza. É isso que impede o caminho novo de mexer no
                // faturamento que já funciona: nota de romaneio continua entrando pelo ramo
                // acima, com o mesmo comportamento de antes.
                var lineUsages = await usageGuard.ValidateAsync(invoice);

                await fiscalAdjustment.ExecuteAsync(
                    invoice, lineUsages, userName, CommitMode.Deferred);
            }

            invoice.InvoiceStatus = InvoiceStatus.Confirmed;
            invoice.ApprovedBy = userName;
            invoice.ApprovedAt = DateTime.Now;

            await db.SaveChangesAsync();

            // Ledger flusheado acima → recálculo das liberações afetadas lê as alocações.
            foreach (var releaseKey in affectedReleaseKeys)
                await recalcShipped.RecalculateAsync(releaseKey);

            // Saldo da carga: só a DEVOLUÇÃO mexe nele aqui. Confirmar uma nota NORMAL não
            // muda nada, porque Pending já consome — o consumo nasce na criação da nota. O
            // "não-gancho" da nota normal é deliberado, não esquecimento.
            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                await loadHook.ApplyAsync(
                    invoice,
                    ShipmentLoadMovementType.Returned,
                    userName,
                    $"Devolução {invoice.InvoiceNumber} confirmada: saldo devolvido à carga.");

                await db.SaveChangesAsync();
            }

            if (commitMode == CommitMode.Auto)
                await db.CommitAsync();
        }
        catch
        {
            // Em Deferred o rollback é do dono da transação: chamá-lo aqui derrubaria trabalho
            // dele que não tem nada a ver com esta nota.
            if (commitMode == CommitMode.Auto)
                await db.RollbackAsync();

            throw;
        }
    }

    private async Task ProcessNormalInvoiceAsync(
        SalesInvoice invoice,
        string userName)
    {
        if (invoice.SalesTransactions == null)
        {
            return;
        }

        foreach (var transaction in invoice.SalesTransactions)
        {
            transaction.TransactionStatus =
                StorageTransactionsStatus.Invoiced;

            transaction.InvoiceNumber =
                invoice.TaxDocumentNumber;

            transaction.InvoiceSerie =
                invoice.TaxDocumentSeries;

            transaction.InvoiceQty =
                transaction.NetWeight;

            transaction.IsInvoiced = true;

            transaction.InvoicedAt = DateTime.Now;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;
        }
    }

    private async Task ProcessReturnInvoiceAsync(
        SalesInvoice returnInvoice,
        string userName,
        HashSet<Guid> affectedReleaseKeys,
        IReadOnlyDictionary<Guid, StorageTransactionsStatus>? shipmentOutcomes = null)
    {
        foreach (var item in returnInvoice.Items)
        {
            ValidateLineItemBalance(item);

            item.DeliveredQuantity = item.Quantity;

            item.DeliveryStatus =
                SalesInvoiceDeliveryStatus.Closed;
        }

        var originInvoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .FirstOrDefaultAsync(
                x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            ?? throw new KeyNotFoundException(
                $"Origin invoice not found.");

        if (originInvoice.InvoiceStatus ==
            InvoiceStatus.Cancelled)
        {
            throw new ApplicationException(
                "Sales invoice origin is cancelled.");
        }

        // A titularidade do status da origem é DESTA operação, não só da criação do retorno
        // (SalesInvoicesReturnService). O estorno devolve a origem para Confirmed; sem
        // regravar aqui, a sequência Retornar → Confirmar → Estornar → Confirmar deixaria a
        // origem presa em Confirmed com o retorno confirmado ao lado. Idempotente de
        // propósito: reescrever o mesmo valor não tem efeito, e o estado se autocorrige.
        //
        // Antes do return abaixo: documento sem romaneio também tem que marcar a origem.
        //
        // CONDICIONAL ao retorno TOTAL: numa devolução PARCIAL (recusa de carga) carimbar a
        // origem como Returned a fecharia — SalesInvoicesReturnService também põe
        // DeliveryStatus = Closed — e a SEGUNDA recusa do mesmo documento morreria em
        // "Invoice closed.", sem saída pela tela. O caminho total avalia true aqui, então
        // continua idêntico ao que era. O saldo não muda nos dois casos: a fórmula da carga
        // conta Confirmed e Returned igual.
        if (await IsFullyReturnedAsync(originInvoice))
        {
            originInvoice.InvoiceStatus = InvoiceStatus.Returned;
        }

        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;

        if (originInvoice.SalesTransactions == null)
        {
            return;
        }

        foreach (var transaction in originInvoice.SalesTransactions)
        {
            // Com destinos informados, só os romaneios escolhidos são tocados: devolver meia
            // nota não pode devolver a carreta inteira do vizinho. Sem eles, todos entram e
            // viram Returned, como sempre foi.
            if (shipmentOutcomes != null &&
                !shipmentOutcomes.TryGetValue(transaction.Key, out _))
            {
                continue;
            }

            if (transaction.SalesShipmentReleaseKey is { } releaseKey)
                affectedReleaseKeys.Add(releaseKey);

            var outcome = shipmentOutcomes is null
                ? StorageTransactionsStatus.Returned
                : shipmentOutcomes[transaction.Key];

            transaction.TransactionStatus = outcome;

            // "Segue viagem": o romaneio precisa voltar ao pool, e o que o mantém fora dele é a
            // nota — ShipmentBillingTransactionGuardService recusa por SalesInvoiceKey != null,
            // antes mesmo de olhar o status. Soltá-la é o que faz o romaneio reaparecer no
            // faturamento e na Montagem de Carga.
            if (outcome == StorageTransactionsStatus.Confirmed)
            {
                transaction.SalesInvoiceKey = null;
                transaction.InvoiceNumber = null;
                transaction.InvoiceSerie = null;
                transaction.InvoicedAt = null;
            }

            transaction.ReturnInvoiceKey =
                returnInvoice.Key;

            transaction.ReturnedAt =
                DateTime.Now;

            transaction.ReturnedBy =
                userName;

            // No destino ARMAZÉM o romaneio segue Invoiced para continuar debitando o armazém de
            // origem, mas o faturamento dele foi desfeito como em qualquer devolução: o volume
            // faturado é zero e a nota está devolvida.
            transaction.IsInvoiced = false;

            transaction.InvoiceQty = 0;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;
        }
    }

    /// <summary>
    /// A origem está inteiramente devolvida? Para CADA item de origem, a soma das devoluções
    /// vivas (não canceladas) — incluindo a que está sendo confirmada agora — tem de alcançar a
    /// quantidade faturada.
    /// </summary>
    /// <remarks>
    /// Item de origem que não aparece em devolução nenhuma derruba o total na hora: uma carga
    /// com dois produtos e só um recusado não pode fechar a nota inteira.
    /// <para>
    /// A devolução em confirmação já está gravada (nasce <c>Pending</c> pelo
    /// <c>SalesInvoicesCreateService</c>) e o filtro é por <c>!= Cancelled</c>, então ela entra
    /// na soma sem precisar ser somada à parte.
    /// </para>
    /// </remarks>
    private async Task<bool> IsFullyReturnedAsync(SalesInvoice originInvoice)
    {
        var originItems = await db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == originInvoice.Key)
            .Select(x => new { x.Key, x.Quantity })
            .ToListAsync();

        if (originItems.Count == 0)
            return true;

        foreach (var originItem in originItems)
        {
            var returned = await db.Context.SalesInvoicesItems
                .AsNoTracking()
                .Where(x => x.SalesInvoiceItemOriginKey == originItem.Key &&
                            x.SalesInvoice!.InvoiceType == SalesInvoiceType.Return &&
                            x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
                .SumAsync(x => (decimal?)x.Quantity) ?? decimal.Zero;

            if (returned < originItem.Quantity - Tolerance)
                return false;
        }

        return true;
    }

    private void ValidateLineItemBalance(
        SalesInvoiceItem item)
    {
        var totalOriginal = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.Key == item.SalesInvoiceItemOriginKey)
            .Select(x => x.Quantity)
            .SingleOrDefault();

        if (totalOriginal <= 0)
        {
            throw new ApplicationException(
                "Original invoice item not found.");
        }

        var totalIncoming = db.Context.SalesInvoicesItems
            .Where(x =>
                x.SalesInvoice.InvoiceType ==
                    SalesInvoiceType.Return &&

                x.SalesInvoice.InvoiceStatus !=
                    InvoiceStatus.Cancelled &&

                x.SalesInvoiceItemOriginKey ==
                    item.SalesInvoiceItemOriginKey &&

                x.Key != item.Key)
            .Sum(x => x.Quantity);

        if (totalIncoming + item.Quantity > totalOriginal)
        {
            throw new ApplicationException(
                "Returned quantity exceeds the original invoice item quantity.");
        }
    }
}