using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesReverseConfirmService(
    IUnitOfWork db,
    SalesContractsAllocationDeleteForInvoiceService allocationDelete,
    ShipmentLoadsBalanceHookService loadHook,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices
            .Include(x => x.Items)
            .Include(x => x.SalesTransactions)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException(
                resource["SALES_INVOICE_NOT_FOUND"].Value);

        if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
        {
            throw new ApplicationException(
                "Invoice is not confirmed.");
        }

        await db.BeginTransactionAsync();

        try
        {
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
                    await ReclaimGoodsFromWarehouseAsync(
                        shipmentLoadKey.Value,
                        invoice,
                        userName);

                    await ReverseLoadReturnAsync(
                        invoice,
                        userName);
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

            invoice.InvoiceStatus = InvoiceStatus.Pending;

            invoice.ApprovedAt = null;
            invoice.ApprovedBy = null;

            // Ledger: estorno de confirmação remove as alocações desta nota (Normal → as
            // alocações padrão; devolução → as linhas negativas, restaurando o consumo) e
            // recalcula contratos/liberações derivado-da-soma, na mesma transação.
            await allocationDelete.ExecuteAsync(invoice.Key, userName, CommitMode.Deferred);

            await db.SaveChangesAsync();

            // Saldo da carga: só a DEVOLUÇÃO mexe. Estornar a confirmação de uma nota NORMAL
            // a devolve para Pending, e Pending continua consumindo — quem desfaz o consumo é
            // cancelar ou excluir, não estornar. Desfazer no mesmo nível em que o efeito foi
            // aplicado.
            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                await loadHook.ApplyAsync(
                    invoice,
                    ShipmentLoadMovementType.ReturnReversed,
                    userName,
                    $"Confirmação da devolução {invoice.InvoiceNumber} estornada: saldo consumido de novo.");

                await db.SaveChangesAsync();
            }

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
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
            entry.TransactionStatus = StorageTransactionsStatus.Cancelled;
            entry.UpdatedAt = DateTime.Now;
            entry.UpdatedBy = userName;
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
    private async Task ReclaimGoodsFromWarehouseAsync(
        Guid shipmentLoadKey, SalesInvoice returnInvoice, string userName)
    {
        var entry = await db.Context.StorageTransactions
            .Where(x => x.RefusedFromShipmentLoadKey == shipmentLoadKey &&
                        x.TransactionType == StorageTransactionType.SalesShipmentReturn &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .FirstOrDefaultAsync();

        if (entry is null)
            return;

        var loadCode = await db.Context.ShipmentLoads
            .Where(x => x.Key == shipmentLoadKey)
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

        var otherConfirmedReturn = await db.Context.SalesInvoices
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey &&
                        x.InvoiceType == SalesInvoiceType.Return &&
                        x.InvoiceStatus == InvoiceStatus.Confirmed &&
                        x.Key != returnInvoice.Key)
            .Select(x => x.InvoiceNumber)
            .FirstOrDefaultAsync();

        if (otherConfirmedReturn != null)
        {
            throw new ApplicationException(
                $"A devolução ao armazém da carga {loadCode} também sustenta o documento de " +
                $"retorno {otherConfirmedReturn}, ainda confirmado. Estorne todos os documentos " +
                "dessa recusa antes de desfazer o crédito de armazém.");
        }

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
            $"{entry.Comments}Estorno da devolução {returnInvoice.InvoiceNumber} por " +
            $"{userName} em {DateTime.Now:dd/MM/yyyy HH:mm}: crédito no armazém desfeito.";
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