using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsReverseService(
    IUnitOfWork db,
    StorageAddressesGetBalanceService balanceService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string username, TransactionCode transactionCode = TransactionCode.StorageTransaction)
    {
        var doc = await db.Context.StorageTransactions
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new NotFoundException(resource["EXCEPTION_00007"].Value);

        // Romaneio montado em carga não é cancelável/estornável por aqui: durante o
        // faturamento PARCIAL ele ainda está Confirmed, então um guard por status deixaria
        // passar e destruiria romaneio já faturado em parte. Por isso a condição é a presença
        // da CARGA, não o status — que é projeção da carga e oscila.
        if (doc.ShipmentLoadKey != null)
        {
            var loadCode = await db.Context.ShipmentLoads
                .Where(x => x.Key == doc.ShipmentLoadKey)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {doc.Code} está montado na carga {loadCode}. " +
                "Cancele a carga na Montagem de Carga antes de  o romaneio.");
        }

        // Mesma proteção do cancelamento: a devolução gerada pela recusa tem ShipmentLoadKey
        // nulo de propósito e escaparia do guard acima. Estorná-la voltaria o romaneio a
        // Pending, tirando o crédito do armazém de destino sem devolver nada à carga.
        if (doc.RefusedFromShipmentLoadKey != null)
        {
            var refusedLoadCode = await db.Context.ShipmentLoads
                .Where(x => x.Key == doc.RefusedFromShipmentLoadKey)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {doc.Code} é a devolução da recusa da carga {refusedLoadCode} e " +
                "não pode ser estornado por aqui.");
        }

        // Mesmo caso, no fluxo LEGADO: a devolução do retorno de um documento de saída sem carga
        // tem AS DUAS colunas acima nulas e escaparia dos dois guards. Quem desfaz esta entrada é
        // o estorno da confirmação da própria devolução, que sabe o que mais precisa voltar.
        if (doc.GeneratedByReturnInvoiceKey != null)
        {
            var invoiceNumber = await db.Context.SalesInvoices
                .Where(x => x.Key == doc.GeneratedByReturnInvoiceKey)
                .Select(x => x.InvoiceNumber)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {doc.Code} é a devolução gerada pelo retorno {invoiceNumber} e " +
                "não pode ser estornado por aqui.");
        }
        
        if (doc.TransactionOrigin == TransactionCode.StorageTransaction)
            transactionCode = TransactionCode.StorageTransaction;    
        
        if (doc.TransactionOrigin != transactionCode)
            throw new ApplicationException(resource["EXCEPTION_00008"].Value + doc.TransactionOrigin);
        
        if (doc.TransactionStatus == StorageTransactionsStatus.Invoiced)
            throw new ApplicationException(resource["EXCEPTION_00009"].Value);
        
        ValidateBalance(doc);

        var hasAllocations = await db.Context.PurchaseContractsAllocations
            .AnyAsync(x => x.StorageTransactionKey == key);

        if (hasAllocations)
        {
            throw new ApplicationException(
                "This storage transaction has purchase contract allocations. Please remove them before reversing.");
        }

        try
        {
            doc.TransactionStatus = StorageTransactionsStatus.Pending;
            doc.CleaningDiscount = 0;
            doc.CleaningServicePrice = 0;
            doc.DryingDiscount = 0;
            doc.DryingServicePrice = 0;
            doc.OthersDicount = 0;
            doc.ShipmentPrice = 0;
            doc.ReceiptServicePrice = 0;
            doc.NetWeight = doc.GrossWeight;

            await db.SaveChangesAsync();

            if (doc.ShipmentReleaseKey.HasValue &&
                ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(doc.TransactionType))
            {
                await recalcShipped.RecalculateAsync(doc.ShipmentReleaseKey.Value);
            }
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private void ValidateBalance(StorageTransaction doc)
    {
        if (string.IsNullOrEmpty(doc.StorageAddressCode))
            return;

        if (doc.TransactionType == StorageTransactionType.Shipment)
            return;
        
        var balance = balanceService.GetBalance(doc.StorageAddressCode);
        if (balance < doc.NetWeight)
            throw new ApplicationException(resource["EXCEPTION_000011"].Value);
    }
}