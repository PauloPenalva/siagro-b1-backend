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
    public async Task ExecuteAsync(Guid key, string userName)
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

        await db.BeginTransactionAsync();

        try
        {
            var affectedReleaseKeys = new HashSet<Guid>();

            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                await ProcessReturnInvoiceAsync(
                    invoice,
                    userName,
                    affectedReleaseKeys);

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

            await db.CommitAsync();
        }
        catch
        {
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
        HashSet<Guid> affectedReleaseKeys)
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
        originInvoice.InvoiceStatus = InvoiceStatus.Returned;
        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;

        if (originInvoice.SalesTransactions == null)
        {
            return;
        }

        foreach (var transaction in originInvoice.SalesTransactions)
        {
            if (transaction.SalesShipmentReleaseKey is { } releaseKey)
                affectedReleaseKeys.Add(releaseKey);

            transaction.TransactionStatus =
                StorageTransactionsStatus.Returned;

            transaction.ReturnInvoiceKey =
                returnInvoice.Key;

            transaction.ReturnedAt =
                DateTime.Now;

            transaction.ReturnedBy =
                userName;

            transaction.IsInvoiced = false;

            transaction.InvoiceQty = 0;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;
        }
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