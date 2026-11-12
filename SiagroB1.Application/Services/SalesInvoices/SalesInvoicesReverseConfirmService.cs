using SiagroB1.Application.Services.StorageInvoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesReverseConfirmService(
    IUnitOfWork db,
    SalesContractsAllocationDeleteForInvoiceService allocationDelete,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ShipmentLoadsBalanceHookService loadHook,
    ShipmentLoadsClosureHookService loadClosureHook,
    IStringLocalizer<Resource> resource,
    FinancialDocumentsSalesFirmSyncService? salesFirmSync = null,
    FinancialDocumentsSalesNoContractService? salesNoContract = null,
    FiscalStockPeriodGuard? periodGuard = null,
    SiagroB1.Application.Services.Accounting.Postings.IAccountingPostingService? accounting = null)
{
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);

    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices
            .Include(x => x.Items)
            .Include(x => x.SalesTransactions)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException(
                resource["SALES_INVOICE_NOT_FOUND"].Value);

        await StorageDepositRules.EnsureRemittanceHasNoReturnAsync(db.Context, key);

        // Devolução própria autorizada só sai pelo cancelamento da NF-e (spec §9.4): sem carga nem
        // ReturnInvoiceKey ela cairia no ramo legado e carimbaria um romaneio alheio como faturado.
        if (invoice.IsNfeReturn)
        {
            throw new DefaultException(
                "A devolução com NF-e não pode ser estornada: ela só sai pelo cancelamento da NF-e (botão Cancelar do documento).");
        }

        // NF-e emitida continua valendo na SEFAZ: desfazer saldo e alocação dela deixaria a nota
        // sem efeito no sistema. O caminho é o cancelamento da NF-e.
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Cancelled)
            throw new DefaultException(NfeLockRules.EmittedReverseMessage);

        if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
        {
            throw new ApplicationException(
                "Invoice is not confirmed.");
        }

        // O estorno devolve a saída ao estoque fiscal do mês gravado: mês fechado recusa.
        await _periodGuard.EnsureOpenAsync(invoice);

        // Spec 2026-10-15: firme composto, ou com dação/encontro de contas, recusa antes de mexer em qualquer coisa.
        if (salesFirmSync is not null)
            await salesFirmSync.EnsureCanUndoAsync(invoice);
        if (salesNoContract is not null)
            await salesNoContract.EnsureCanUndoAsync(invoice.Key);

        await db.BeginTransactionAsync();

        try
        {
            // Retornos da MESMA recusa estornados junto com este (ver ReclaimGoodsFromWarehouseAsync).
            var siblings = new List<SalesInvoice>();

            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                /*
                 * Detecta se devolução é:
                 * DE CARGA, NOVA ou LEGADA
                 */

                // A devolução nascida de CARGA precisa ser testada PRIMEIRO. Ela nunca grava
                // ReturnInvoiceKey em romaneio nenhum (a nota de carga tem SalesTransactions
                // vazia — chega ao romaneio pela carga), então isNewFlow é sempre false e ela
                // caía no ramo LEGADO, cuja consulta de "órfãos" — SalesInvoiceKey nulo +
                // ShipmentLoadKey nulo + Confirmed + mesmo CardCode, casada só por ItemCode —
                // SEQUESTRA romaneios soltos de outro carregamento, carimba-os como Invoiced e
                // os anexa a esta nota de origem.
                var shipmentLoadKey =
                    await SalesInvoiceOriginResolver.ResolveShipmentLoadKeyAsync(db.Context, invoice);

                var isNewFlow = await db.Context.StorageTransactions
                    .AnyAsync(x =>
                        x.ReturnInvoiceKey == invoice.Key);

                if (shipmentLoadKey != null)
                {
                    siblings = await ReclaimGoodsFromWarehouseAsync(
                        shipmentLoadKey.Value,
                        invoice,
                        userName);

                    // Uma recusa tem um destino só: no máximo uma das duas entradas lista a origem.
                    if (siblings.Count == 0)
                        siblings = await ReclaimTransitLossAsync(
                            shipmentLoadKey.Value,
                            invoice,
                            userName);

                    // Os irmãos passam pelas mesmas travas do documento estornado.
                    foreach (var sibling in siblings)
                    {
                        if (sibling.IsNfeReturn)
                            throw new DefaultException(
                                $"A devolução {sibling.InvoiceNumber}, da mesma recusa, tem NF-e e só sai pelo cancelamento da NF-e.");
                        if (sibling.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Cancelled)
                            throw new DefaultException(NfeLockRules.EmittedReverseMessage);
                        await _periodGuard.EnsureOpenAsync(sibling);
                        if (salesFirmSync is not null)
                            await salesFirmSync.EnsureCanUndoAsync(sibling);
                        if (salesNoContract is not null)
                            await salesNoContract.EnsureCanUndoAsync(sibling.Key);
                    }

                    foreach (var doc in siblings.Prepend(invoice))
                    {
                        await ReverseLoadReturnAsync(
                            doc,
                            userName);
                    }
                }
                else if (isNewFlow)
                {
                    await ReverseNewReturnAsync(
                        invoice,
                        userName);
                }
                else
                {
                    await ReverseLegacyReturnAsync(
                        invoice,
                        userName);
                }
            }
            else
            {
                await ReverseNormalInvoiceAsync(
                    invoice,
                    userName);
            }

            foreach (var doc in siblings.Prepend(invoice))
            {
                if (doc != invoice)
                {
                    doc.UpdatedAt = DateTime.Now;
                    doc.UpdatedBy = userName;
                    doc.Comments =
                        $"{doc.Comments} Confirmação estornada junto com a devolução " +
                        $"{invoice.InvoiceNumber} (mesma recusa) por {userName} em {DateTime.Now:dd/MM/yyyy HH:mm}.";
                }

                await UndoConfirmationAsync(doc, userName);
            }

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Efeitos comuns do estorno de UM documento (contabilização, status, alocações, títulos,
    /// saldo e situação da carga). Separado para valer também para os retornos da mesma recusa.
    /// </summary>
    private async Task UndoConfirmationAsync(SalesInvoice doc, string userName)
    {
        // Estorno da contabilização (630 itens / 635 total); sem lançamento da confirmação, não faz nada.
        if (accounting is not null)
        {
            var postingLines = await db.Context.SalesInvoicesItems.AsNoTracking()
                .Where(l => l.SalesInvoiceKey == doc.Key).ToListAsync();
            var (itemsCtx, documentCtx) = await Accounting.Postings.SalesInvoicePostingContext.BuildAsync(db, doc,
                postingLines, Accounting.Postings.SalesInvoicePostingContext.UndoEvent, DateTime.Today, false);
            await accounting.PostReversalAsync("630", itemsCtx, Accounting.Postings.SalesInvoicePostingContext.ConfirmEvent, userName);
            await accounting.PostReversalAsync("635", documentCtx, Accounting.Postings.SalesInvoicePostingContext.ConfirmEvent, userName);
        }

        doc.InvoiceStatus = InvoiceStatus.Pending;

        doc.ApprovedAt = null;
        doc.ApprovedBy = null;

        // GAC-1171 (rateio): a devolução estornada deixa de contar. Vale para os três ramos
        // (carga, novo e legado), por isso fica aqui e não dentro de um deles.
        if (doc.InvoiceType == SalesInvoiceType.Return)
            await SalesInvoicesRecalculateReturnedService.RecalculateAsync(
                db.Context, doc.SalesInvoiceOriginKey);

        // Ledger: estorno de confirmação remove as alocações desta nota (Normal → as
        // alocações padrão; devolução → as linhas negativas, restaurando o consumo) e
        // recalcula contratos/liberações derivado-da-soma, na mesma transação.
        await allocationDelete.ExecuteAsync(doc.Key, userName, CommitMode.Deferred);

        await db.SaveChangesAsync();

        // Spec 2026-10-15 (ponto 3): o documento voltou a Pendente — os títulos soltos das linhas são cancelados.
        if (salesFirmSync is not null)
        {
            await salesFirmSync.EnqueueSyncAsync(
                doc.Items.Where(i => i.Key != null).Select(i => i.Key!.Value).ToList(), userName);
            await db.SaveChangesAsync();
        }

        // Spec 2026-10-19 parte 1: documento de volta a Pendente — títulos sem contrato cancelados.
        if (salesNoContract is not null)
        {
            salesNoContract.EnqueueUndo(doc.Key, userName);
            await db.SaveChangesAsync();
        }

        // Spec 2026-10-19 parte 3: situação da fatura de armazenagem que este documento fatura.
        await StorageInvoicesRecalculateStatusService.RecalculateForSalesInvoiceAsync(db.Context, doc.Key);
        await db.SaveChangesAsync();

        // Saldo da carga: só a DEVOLUÇÃO mexe. Estornar a confirmação de uma nota NORMAL
        // a devolve para Pending, e Pending continua consumindo — quem desfaz o consumo é
        // cancelar ou excluir, não estornar. Desfazer no mesmo nível em que o efeito foi
        // aplicado.
        if (doc.InvoiceType == SalesInvoiceType.Return)
        {
            await loadHook.ApplyAsync(
                doc,
                ShipmentLoadMovementType.ReturnReversed,
                userName,
                $"Confirmação da devolução {doc.InvoiceNumber} estornada: saldo consumido de novo.");

            await db.SaveChangesAsync();
        }
        // GAC-1171 (melhorias): o saldo da nota NORMAL não muda, mas a situação da carga sim.
        // A Concluída exige que nenhuma nota Normal da carga esteja Pendente, e o estorno
        // acabou de devolver esta a Pendente: sem o recálculo, a carga seguiria Concluída com
        // uma nota fora da Conferência. O gancho da situação recalcula e registra o log, sem
        // movimento, porque o saldo é o mesmo.
        else
        {
            await loadClosureHook.ApplyAsync(doc, userName);

            await db.SaveChangesAsync();
        }
    }

    private async Task ReverseNormalInvoiceAsync(
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
                StorageTransactionsStatus.Confirmed;

            transaction.InvoiceNumber = null;
            transaction.InvoiceSerie = null;
            transaction.InvoiceQty = 0;

            transaction.IsInvoiced = false;

            transaction.InvoicedAt = null;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;
        }
    }

    /*
     * FLUXO NOVO
     */
    private async Task ReverseNewReturnAsync(
        SalesInvoice returnInvoice,
        string userName)
    {
        var transactions = await db.Context.StorageTransactions
            .Where(x =>
                x.ReturnInvoiceKey == returnInvoice.Key)
            .ToListAsync();

        var originInvoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            ?? throw new ApplicationException(
                "Origin invoice not found.");

        // O estorno NÃO mexe no status nem na entrega da origem: esses efeitos nascem com a
        // CRIAÇÃO do retorno e só o cancelamento/exclusão os desfaz
        // (SalesInvoicesReturnOriginRestoreService). Aqui o retorno volta a Pendente, mas
        // continua existindo — a origem segue retornada.
        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;

        foreach (var transaction in transactions)
        {
            // ⚠️ Romaneio JÁ FATURADO em outra nota não volta. Depois de um retorno "segue
            // viagem" ele fica solto e disponível — que é o objetivo do destino — e pode ter sido
            // faturado noutro documento. O ReturnInvoiceKey da devolução antiga continua nele, e é
            // por ele que a consulta acima o encontra: re-anexá-lo aqui deixaria o mesmo volume
            // faturado em duas notas, que é exatamente o sequestro que o ramo legado já cometeu.
            if (transaction.SalesInvoiceKey is { } currentInvoiceKey &&
                currentInvoiceKey != originInvoice.Key)
            {
                continue;
            }

            transaction.TransactionStatus =
                StorageTransactionsStatus.Invoiced;

            transaction.ReturnInvoiceKey = null;

            transaction.ReturnedAt = null;
            transaction.ReturnedBy = null;

            transaction.IsInvoiced = true;

            transaction.InvoiceNumber =
                originInvoice.TaxDocumentNumber;

            transaction.InvoiceSerie =
                originInvoice.TaxDocumentSeries;

            transaction.InvoiceQty =
                transaction.NetWeight;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;

            if (!originInvoice.SalesTransactions
                    .Any(x => x.Key == transaction.Key))
            {
                originInvoice.SalesTransactions
                    .Add(transaction);
            }
        }

        await CancelWarehouseReturnAsync(returnInvoice, userName);

        foreach (var item in returnInvoice.Items)
        {
            item.DeliveredQuantity = 0;

            item.DeliveryStatus =
                SalesInvoiceDeliveryStatus.Open;
        }
    }

    /// <summary>
    /// Cancela a devolução ao armazém que este retorno gerou, se houver.
    /// </summary>
    /// <remarks>
    /// O crédito de estoque não pode ficar de pé sozinho: com o retorno de volta a Pendente, a
    /// nota de origem volta a valer, e o grão estaria ao mesmo tempo vendido e no armazém.
    /// <para>
    /// A busca é por <c>GeneratedByReturnInvoiceKey</c>, que aponta ESTE documento de retorno —
    /// e não a origem. Uma nota retornada em parcelas tem uma devolução por parcela, e pela
    /// origem este estorno cancelaria todas.
    /// </para>
    /// <para>
    /// Cancelado à mão, e não pelo <c>StorageTransactionsCancelService</c>: aquele serviço RECUSA
    /// esta transação de propósito (é o guard que impede o operador de derrubar o crédito pela
    /// tela de Romaneios). Quem tem autoridade para desfazê-la é exatamente este caminho.
    /// </para>
    /// </remarks>
    private async Task CancelWarehouseReturnAsync(SalesInvoice returnInvoice, string userName)
    {
        var entries = await db.Context.StorageTransactions
            .Where(x => x.GeneratedByReturnInvoiceKey == returnInvoice.Key &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .ToListAsync();

        foreach (var entry in entries)
        {
            // ANTES de cancelar: se a mercadoria devolvida já foi reembarcada, o estorno é
            // recusado inteiro e nada é gravado.
            await CancelReturnReleasesAsync(
                entry, userName, $"retorno {returnInvoice.InvoiceNumber}");

            entry.TransactionStatus = StorageTransactionsStatus.Cancelled;
            entry.UpdatedAt = DateTime.Now;
            entry.UpdatedBy = userName;
        }
    }

    /// <summary>
    /// Desfaz as liberações de embarque que esta devolução emitiu, e RECUSA o estorno quando a
    /// mercadoria devolvida já foi reembarcada.
    /// </summary>
    /// <remarks>
    /// A liberação é a porta de saída do grão que voltou. Cancelar a devolução sem olhar para ela
    /// deixaria uma liberação ativa apontando mercadoria que não está mais no armazém — e a
    /// Expedição de Grãos ofereceria grão inexistente.
    /// <para>
    /// O volume vem de <c>CalculateShippedAsync</c>, e não da coluna persistida: o
    /// <c>ShippedQuantity</c> é derivado e pode estar defasado se um hook não tiver rodado.
    /// </para>
    /// <para>
    /// Cancela À MÃO, e não por <c>ShipmentReleasesCancelationService</c>: aquele recusa quando o
    /// contrato está <c>Finished</c> e quando não há saldo a devolver — duas condições que não se
    /// aplicam a uma liberação que não consome contrato nenhum. Mesmo espírito do cancelamento
    /// direto da entrada de armazém, logo acima.
    /// </para>
    /// </remarks>
    private async Task CancelReturnReleasesAsync(
        StorageTransaction entry, string userName, string context)
    {
        var releases = await db.Context.ShipmentReleases
            .Where(x => x.GeneratedByStorageTransactionKey == entry.Key &&
                        x.Status != ReleaseStatus.Cancelled)
            .ToListAsync();

        foreach (var release in releases)
        {
            var shipped = await recalcShipped.CalculateShippedAsync(release.Key, release.Origin);

            if (shipped > decimal.Zero)
            {
                throw new ApplicationException(
                    $"A mercadoria devolvida pelo romaneio {entry.Code} já foi embarcada de novo " +
                    $"({shipped:N3} de {release.ReleasedQuantity:N3}, pela liberação " +
                    $"{release.DocNumber}). Estorne esse embarque antes de desfazer a devolução.");
            }

            release.Status = ReleaseStatus.Cancelled;
            release.CancellationReason = $"Estorno da devolução ao armazém ({context}).";
            release.CanceledAt = DateTime.Now;
            release.CanceledBy = userName;
            release.UpdatedAt = DateTime.Now;
            release.UpdatedBy = userName;
        }
    }

    /// <summary>
    /// Desfaz o crédito de armazém de uma devolução nascida de recusa de carga, quando isso ainda
    /// pode ser feito com segurança.
    /// </summary>
    /// <remarks>
    /// O descarregamento é um fato FÍSICO consumado — o grão foi para o armazém de destino e está
    /// creditado no saldo dele, que é agregado (fungível, sem rastreio por lote). Desfazer esse
    /// crédito às cegas arriscaria saldo NEGATIVO se parte dele já tiver saído de novo por outra
    /// operação depois da recusa. Por isso a correção só é aplicada quando o saldo ATUAL do
    /// armazém ainda comporta debitar de volta a quantidade creditada — abaixo disso, a devolução
    /// permanece bloqueada, com uma mensagem que nomeia o que falta em vez de uma parede
    /// incondicional.
    /// <para>
    /// A trava por saldo é POR ARMAZÉM/ITEM, não por esta transação isolada: se outra operação já
    /// consumiu o crédito (ex.: um novo embarque saiu do mesmo armazém), o saldo agregado já não
    /// comporta a devolução, ainda que a transação em si continue <c>Confirmed</c>.
    /// </para>
    /// <para>
    /// <b>Guard de recusa em lote:</b> uma única recusa pode agrupar vários documentos de retorno
    /// numa única entrada de armazém (<c>ShipmentLoadsRefuseService.ReturnToWarehouseAsync</c>
    /// soma todos de uma vez). Cancelar essa entrada por causa de UM documento derrubaria o
    /// lastro dos OUTROS, que continuam confirmados — por isso a correção é recusada por inteiro
    /// enquanto existir outro documento de retorno vivo da mesma carga.
    /// </para>
    /// <para>
    /// Quando aplicada, a transação é marcada <c>Cancelled</c> (nunca apagada, para manter o
    /// rastro) — nunca pelo <c>StorageTransactionsCancelService</c>, que RECUSA esta transação de
    /// propósito (é o guard que impede o operador de derrubar o crédito pela tela de Romaneios).
    /// Quem tem autoridade para desfazê-la é exatamente este caminho, no mesmo espírito de
    /// <c>CancelWarehouseReturnAsync</c> para o fluxo legado.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <c>null</c> quando a recusa não teve entrada de armazém; senão, os outros retornos
    /// CONFIRMADOS da mesma recusa, que o chamador estorna junto. Antes a
    /// trava exigia que eles já estivessem estornados, mas cada um exigia o outro — uma recusa
    /// de dois documentos ao armazém nunca saía (CG001844, 9639/9640).
    /// </returns>
    private async Task<List<SalesInvoice>?> ReclaimGoodsFromWarehouseAsync(
        Guid shipmentLoadKey, SalesInvoice returnInvoice, string userName)
    {
        var entry = await FindRefusalEntryAsync(
            shipmentLoadKey, returnInvoice, StorageTransactionType.SalesShipmentReturn, allowUnlisted: true);

        // Retorno de recusa sem destino armazém ("segue viagem"): não há crédito a desfazer.
        if (entry is null)
            return [];

        var entryDocuments = RefusalEntryDocuments.Parse(entry.Comments);

        // Primeiro guard, ANTES do saldo de armazém: se a mercadoria já foi reembarcada, o saldo
        // também estará curto e o operador receberia a mensagem genérica "parte já foi consumida
        // por outra operação" em vez da acionável "estorne o embarque primeiro".
        await CancelReturnReleasesAsync(
            entry, userName, $"recusa da carga, retorno {returnInvoice.InvoiceNumber}");

        var loadCode = await db.Context.ShipmentLoads
            .Where(x => x.Key == shipmentLoadKey)
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

        var siblings = await FindRefusalSiblingsAsync(shipmentLoadKey, returnInvoice, entryDocuments);

        var warehouseBalance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            db.Context, entry.WarehouseCode, entry.ItemCode);

        if (warehouseBalance < entry.NetWeight)
        {
            throw new ApplicationException(
                $"A mercadoria da carga {loadCode} foi devolvida ao armazém {entry.WarehouseCode} " +
                $"pelo romaneio {entry.Code} e já está creditada no saldo dele. Saldo do armazém " +
                $"hoje ({warehouseBalance:N3}) menor que a quantidade a debitar de volta " +
                $"({entry.NetWeight:N3}) — parte já foi consumida por outra operação. " +
                "A devolução não pode ser estornada.");
        }

        entry.TransactionStatus = StorageTransactionsStatus.Cancelled;
        entry.CanceledAt = DateTime.Now;
        entry.CanceledBy = userName;
        entry.UpdatedAt = DateTime.Now;
        entry.UpdatedBy = userName;
        entry.Comments =
            $"{entry.Comments}Estorno da devolução " +
            string.Join(", ", siblings.Select(x => x.InvoiceNumber).Prepend(returnInvoice.InvoiceNumber)) +
            " por " +
            $"{userName} em {DateTime.Now:dd/MM/yyyy HH:mm}: crédito no armazém desfeito.";

        return siblings;
    }

    /// <summary>
    /// Desfaz a quebra de trajeto (GAC-1213) de uma devolução nascida de recusa com destino
    /// <see cref="RefusalDestination.TransitLoss"/>: cancela o romaneio 18 daquela recusa.
    /// </summary>
    /// <remarks>
    /// Sem conferência de saldo de armazém e sem liberação a cancelar: a quebra não creditou nada
    /// em lugar nenhum. O romaneio é cancelado à mão, como em <see cref="ReclaimGoodsFromWarehouseAsync"/>
    /// — os serviços de cancelamento/estorno de romaneio recusam entradas de recusa de propósito.
    /// <c>TransitLossQuantity</c> e a situação da carga são recalculados pelo gancho de saldo em
    /// <see cref="UndoConfirmationAsync"/>, depois do SaveChanges (a soma é feita no servidor).
    /// </remarks>
    /// <returns>Os outros retornos CONFIRMADOS da mesma recusa, que o chamador estorna junto.</returns>
    private async Task<List<SalesInvoice>> ReclaimTransitLossAsync(
        Guid shipmentLoadKey, SalesInvoice returnInvoice, string userName)
    {
        // O romaneio 18 sempre grava a lista "Documento(s)": sem a regra antiga de entrada sem lista.
        var entry = await FindRefusalEntryAsync(
            shipmentLoadKey, returnInvoice, StorageTransactionType.TransitLoss, allowUnlisted: false);

        if (entry is null)
            return [];

        var siblings = await FindRefusalSiblingsAsync(
            shipmentLoadKey, returnInvoice, RefusalEntryDocuments.Parse(entry.Comments));

        entry.TransactionStatus = StorageTransactionsStatus.Cancelled;
        entry.CanceledAt = DateTime.Now;
        entry.CanceledBy = userName;
        entry.UpdatedAt = DateTime.Now;
        entry.UpdatedBy = userName;
        entry.Comments =
            $"{entry.Comments} Estorno da devolução " +
            string.Join(", ", siblings.Select(x => x.InvoiceNumber).Prepend(returnInvoice.InvoiceNumber)) +
            $" por {userName} em {DateTime.Now:dd/MM/yyyy HH:mm}: quebra de trajeto desfeita.";

        return siblings;
    }

    /// <summary>
    /// A entrada (do tipo informado) da recusa que gerou este retorno, ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Uma carga pode ter várias recusas (ex.: CG001844 — a primeira "segue viagem", a segunda
    /// "volta ao armazém"). O único vínculo gravado entre a entrada e os documentos da recusa é a
    /// lista "Documento(s): ..." no Comments (ShipmentLoadsRefuseService). A entrada certa é a que
    /// lista a ORIGEM deste retorno; com <paramref name="allowUnlisted"/>, uma entrada sem lista
    /// legível (dado antigo) vale pela regra antiga (uma entrada por carga).
    /// </remarks>
    private async Task<StorageTransaction?> FindRefusalEntryAsync(
        Guid shipmentLoadKey, SalesInvoice returnInvoice, StorageTransactionType type, bool allowUnlisted)
    {
        var entries = await db.Context.StorageTransactions
            .Where(x => x.RefusedFromShipmentLoadKey == shipmentLoadKey &&
                        x.TransactionType == type &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .ToListAsync();

        var originNumber = await db.Context.SalesInvoices
            .Where(x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            .Select(x => x.InvoiceNumber)
            .FirstOrDefaultAsync();

        return entries.FirstOrDefault(e =>
                   originNumber != null &&
                   RefusalEntryDocuments.Parse(e.Comments)?.Contains(originNumber) == true)
               ?? (allowUnlisted
                   ? entries.FirstOrDefault(e => RefusalEntryDocuments.Parse(e.Comments) is null)
                   : null);
    }

    /// <summary>
    /// Os outros retornos CONFIRMADOS da carga cuja origem está na lista da entrada — os da MESMA
    /// recusa. Sem lista (dado antigo), todos os retornos confirmados da carga contam.
    /// </summary>
    private async Task<List<SalesInvoice>> FindRefusalSiblingsAsync(
        Guid shipmentLoadKey, SalesInvoice returnInvoice, IReadOnlySet<string>? entryDocuments)
    {
        var candidates = await db.Context.SalesInvoices
            .Include(x => x.Items)
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey &&
                        x.InvoiceType == SalesInvoiceType.Return &&
                        x.InvoiceStatus == InvoiceStatus.Confirmed &&
                        x.Key != returnInvoice.Key)
            .ToListAsync();

        var originNumbers = await db.Context.SalesInvoices
            .Where(o => candidates.Select(c => c.SalesInvoiceOriginKey).Contains(o.Key))
            .Select(o => new { o.Key, o.InvoiceNumber })
            .ToDictionaryAsync(o => o.Key, o => o.InvoiceNumber);

        return candidates
            .Where(x => entryDocuments is null ||
                        (x.SalesInvoiceOriginKey is { } ok &&
                         originNumbers.TryGetValue(ok, out var number) &&
                         number != null && entryDocuments.Contains(number)))
            .ToList();
    }

    /*
     * FLUXO DA CARGA
     */
    /// <summary>
    /// Estorna a confirmação de uma devolução nascida de CARGA.
    /// </summary>
    /// <remarks>
    /// <b>Não toca em romaneio nenhum, e é esse o ponto.</b> No fluxo da carga o romaneio não
    /// pertence à nota: ele pertence à carga, e seu <c>TransactionStatus</c> é projeção de
    /// <c>ShipmentLoadsRecalculateInvoicedService</c>. Reescrevê-lo aqui brigaria com o escritor
    /// único; procurá-lo pelos critérios do fluxo legado sequestraria romaneio alheio.
    /// <para>
    /// O saldo da carga é restaurado pelo <c>loadHook.ApplyAsync(..., ReturnReversed, ...)</c>
    /// do chamador, que recalcula a partir das notas — a devolução volta a <c>Pending</c> e
    /// deixa de abater a origem.
    /// </para>
    /// <para>
    /// Como nos outros dois ramos, o estorno NÃO desfaz o que a CRIAÇÃO do retorno aplicou na
    /// origem: quem restaura status e entrega é o cancelamento/exclusão
    /// (<c>SalesInvoicesReturnOriginRestoreService</c>).
    /// </para>
    /// </remarks>
    private async Task ReverseLoadReturnAsync(
        SalesInvoice returnInvoice,
        string userName)
    {
        var originInvoice = await db.Context.SalesInvoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            ?? throw new ApplicationException(
                "Origin invoice not found.");

        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;

        foreach (var item in returnInvoice.Items)
        {
            item.DeliveredQuantity = 0;

            item.DeliveryStatus =
                SalesInvoiceDeliveryStatus.Open;
        }
    }

    /*
     * FLUXO LEGADO
     */
    private async Task ReverseLegacyReturnAsync(
        SalesInvoice returnInvoice,
        string userName)
    {
        var originInvoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            ?? throw new ApplicationException(
                "Origin invoice not found.");

        // Mesma regra do fluxo novo: o estorno não desfaz o que a criação do retorno aplicou
        // na origem.
        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;

        var orphanTransactions =
            await db.Context.StorageTransactions
                .Where(x =>
                    x.SalesInvoiceKey == null &&
                    // Romaneio montado em carga NÃO é órfão. O critério abaixo (mesmo cliente,
                    // mesmo produto) é largo o bastante para sequestrar um romaneio de outra
                    // carga, possivelmente já faturada.
                    x.ShipmentLoadKey == null &&
                    // Nem romaneio nascido de/substituído por uma troca de liberação
                    // (GAC-1177): mesmo critério largo, mesmo risco de sequestro — quem controla
                    // esses romaneios é o fluxo da troca.
                    x.ReplacedByShippingReleaseChangeKey == null &&
                    x.ShippingReleaseChangeKey == null &&
                    x.TransactionStatus ==
                        StorageTransactionsStatus.Confirmed &&
                    x.CardCode ==
                        originInvoice.CardCode)
                .ToListAsync();

        var matchedTransactions =
            orphanTransactions
                .Where(x =>
                    returnInvoice.Items.Any(i =>
                        i.ItemCode == x.ItemCode))
                .ToList();

        foreach (var transaction in matchedTransactions)
        {
            transaction.SalesInvoiceKey =
                originInvoice.Key;

            transaction.TransactionStatus =
                StorageTransactionsStatus.Invoiced;

            transaction.InvoiceNumber =
                originInvoice.TaxDocumentNumber;

            transaction.InvoiceSerie =
                originInvoice.TaxDocumentSeries;

            transaction.InvoiceQty =
                transaction.NetWeight;

            transaction.IsInvoiced = true;

            transaction.InvoicedAt ??=
                DateTime.Now;

            transaction.UpdatedAt =
                DateTime.Now;

            transaction.UpdatedBy =
                userName;

            originInvoice.SalesTransactions ??= [];

            if (!originInvoice.SalesTransactions
                    .Any(x => x.Key == transaction.Key))
            {
                originInvoice.SalesTransactions
                    .Add(transaction);
            }
        }

        foreach (var item in returnInvoice.Items)
        {
            item.DeliveredQuantity = 0;

            item.DeliveryStatus =
                SalesInvoiceDeliveryStatus.Open;
        }
    }
}