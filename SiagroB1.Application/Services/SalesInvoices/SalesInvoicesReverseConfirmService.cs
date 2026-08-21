using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.ShipmentLoads;
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
                 * NOVA ou LEGADA
                 */
                var isNewFlow = await db.Context.StorageTransactions
                    .AnyAsync(x =>
                        x.ReturnInvoiceKey == invoice.Key);

                if (isNewFlow)
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