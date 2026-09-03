using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsCancelService(
    IUnitOfWork db,
    ShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task ExecuteAsync(Guid key, string username, TransactionCode transactionCode = TransactionCode.StorageTransaction)
    {
        var doc = await db.Context.StorageTransactions
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new NotFoundException("Storage transaction not found.");

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

        // Devolução gerada pela RECUSA de uma carga. Ela tem ShipmentLoadKey NULO de propósito
        // (senão infla o volume embarcado da carga), então escapava do guard acima — e
        // cancelá-la por aqui derrubaria em silêncio o ReturnedToWarehouseQuantity da carga,
        // reabrindo-a para faturamento com a mercadoria já creditada em outro armazém.
        if (doc.RefusedFromShipmentLoadKey != null)
        {
            var refusedLoadCode = await db.Context.ShipmentLoads
                .Where(x => x.Key == doc.RefusedFromShipmentLoadKey)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {doc.Code} é a devolução da recusa da carga {refusedLoadCode} e " +
                "não pode ser cancelado por aqui.");
        }

        // Mesmo caso, no fluxo LEGADO: a devolução gerada pelo retorno de um documento de saída
        // sem carga tem AS DUAS colunas acima nulas e escaparia dos dois guards. Cancelá-la por
        // aqui derrubaria o crédito do armazém em silêncio, e o grão ficaria sem lugar nenhum —
        // fora da nota, que está devolvida, e fora do estoque.
        if (doc.GeneratedByReturnInvoiceKey != null)
        {
            var invoiceNumber = await db.Context.SalesInvoices
                .Where(x => x.Key == doc.GeneratedByReturnInvoiceKey)
                .Select(x => x.InvoiceNumber)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {doc.Code} é a devolução gerada pelo retorno {invoiceNumber} e " +
                "não pode ser cancelado por aqui.");
        }

        if (doc.TransactionOrigin != transactionCode)
        {
            var msg =
                "This storage transaction is created by another transaction. It cannot be canceled using this method.\n" +
                "Transaction Origin: " + doc.TransactionOrigin;

            throw new ApplicationException(msg);
        }

        if (doc.TransactionStatus == StorageTransactionsStatus.Invoiced)
        {
            throw new ApplicationException("Storage transaction is invoiced. Please, cancel assinged invoice first.");
        }

        var hasAllocations = await db.Context.PurchaseContractsAllocations
            .AnyAsync(x => x.StorageTransactionKey == key);

        if (hasAllocations)
        {
            throw new ApplicationException(
                "This storage transaction has purchase contract allocations. Please remove them before canceling.");
        }

        try
        {
            doc.TransactionStatus = StorageTransactionsStatus.Cancelled;
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
}