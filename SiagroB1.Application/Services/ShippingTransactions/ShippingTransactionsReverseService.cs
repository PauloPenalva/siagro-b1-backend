using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShippingTransactions;

/// <summary>
/// Estorno da Expedição de Grãos: desfaz o par Purchase/SalesShipment criado por
/// <see cref="ShippingTransactionsCreateService"/>, devolvendo o saldo ao contrato de compra e
/// à liberação de embarque. Acionado pela Montagem de Carga, sobre romaneio ainda solto.
/// </summary>
/// <remarks>
/// Era <c>ShipmentBilling/ShipmentBillingDeleteService</c>, acionado pela tela de faturamento.
/// Mudou de lugar junto com o botão: quem fatura passa a lidar com CARGAS, e o romaneio solto
/// (o que ainda dá para estornar) só existe na Montagem.
/// </remarks>
public class ShippingTransactionsReverseService(
    IUnitOfWork db,
    StorageTransactionsGetService storageTransactionsGetService,
    PurchaseContractsAllocationDeleteService  purchaseContractsAllocationDeleteService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ILogger<ShippingTransactionsReverseService> logger)
{
    public async Task ExecuteAsync(Guid key, string username)
    {
        var shipping = await db.Context.ShippingTransactions
                           .Include(x => x.PurchaseStorageTransaction)
                           .Include(x => x.SalesStorageTransaction)
                .FirstOrDefaultAsync(x => x.SalesStorageTransactionKey == key) ??
            throw new NotFoundException("Shipping not found.");

        if (shipping.SalesStorageTransaction is { TransactionStatus: StorageTransactionsStatus.Invoiced })
        {
            throw new ApplicationException("Sales transaction already invoiced.");
        }

        // Romaneio já DEVOLVIDO pelo retorno de um documento de saída: aquele fluxo já desfez o
        // embarque no eixo dele, e estornar por cima reverteria o mesmo volume duas vezes —
        // cancelando a perna de COMPRA e devolvendo o saldo da liberação de novo.
        // O guard de Invoiced acima não pega (o status é Returned) e o da CARGA logo abaixo
        // também não (no fluxo legado ShipmentLoadKey é nulo). Até aqui a recusa existia só no
        // controller do frontend, então a action chamada direto passava reto.
        if (shipping.SalesStorageTransaction is { TransactionStatus: StorageTransactionsStatus.Returned })
        {
            throw new ApplicationException(
                $"O romaneio {shipping.SalesStorageTransaction.Code} já foi devolvido e não pode " +
                "ser estornado. Cancele ou exclua o documento de retorno para desfazer a devolução.");
        }
        // Romaneio já montado em carga não volta por aqui: o estorno cancela o par e devolve os
        // saldos à origem, arrancando volume de baixo de uma carga possivelmente já faturada em
        // parte. Guard pela presença da CARGA e não pelo status: no faturamento parcial o
        // romaneio ainda está Confirmed, e o guard de Invoiced acima deixaria passar.
        if (shipping.SalesStorageTransaction is { ShipmentLoadKey: not null })
        {
            var loadCode = await db.Context.ShipmentLoads
                .Where(x => x.Key == shipping.SalesStorageTransaction.ShipmentLoadKey)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {shipping.SalesStorageTransaction.Code} está montado na carga {loadCode}. " +
                "Cancele a carga antes de estornar o romaneio.");
        }

        
        if (shipping.PurchaseStorageTransaction is { TransactionStatus: StorageTransactionsStatus.Invoiced })
        {
            throw new ApplicationException("Purchase transaction already invoiced.");
        }
        
        try
        {
            // Embarque de liberação de transferência de titularidade não tem perna de
            // compra: a Expedição criou só a saída. Ver ShippingTransactionsCreateService.
            var purchaseStorageTransactionKey = shipping.PurchaseStorageTransactionKey;
            var salesStorageTransactionKey =  shipping.SalesStorageTransactionKey;

            await db.BeginTransactionAsync();

            shipping.PurchaseStorageTransactionKey = null;
            shipping.SalesStorageTransactionKey = Guid.Empty;

            db.Context.ShippingTransactions.Remove(shipping);

            var purchase = purchaseStorageTransactionKey.HasValue
                ? await storageTransactionsGetService.GetByIdAsync(purchaseStorageTransactionKey.Value)
                : null;
            var sales = await storageTransactionsGetService.GetByIdAsync(salesStorageTransactionKey);

            // Sem perna de compra não há alocação a desfazer, e isso é intencional: a
            // alocação do contrato pertence à TRANSFERÊNCIA, não a este embarque. O grão
            // continua sendo da companhia, no lote — só voltou a não estar embarcado.
            // Desfazê-la aqui devolveria saldo de contrato que deve seguir debitado; quem
            // devolve é o cancelamento da transferência.
            if (purchaseStorageTransactionKey.HasValue)
            {
                var purchaseContractAllocKey = await db.Context.PurchaseContractsAllocations
                    .Where(x => x.StorageTransactionKey == purchaseStorageTransactionKey.Value)
                    .Select(x => x.Key)
                    .FirstOrDefaultAsync();

                if (purchaseContractAllocKey != Guid.Empty)
                    await purchaseContractsAllocationDeleteService.ExecuteAsync(purchaseContractAllocKey, username);

                purchase!.TransactionStatus = StorageTransactionsStatus.Cancelled;
                purchase.CanceledAt = DateTime.Now;
                purchase.CanceledBy = username;
            }

            sales.TransactionStatus = StorageTransactionsStatus.Cancelled;
            sales.CanceledAt = DateTime.Now;
            sales.CanceledBy = username;
            
            await db.SaveChangesAsync();

            // O estorno cancela o par escrevendo o status direto, sem passar por
            // StorageTransactionsCancelService — que é onde vive o hook de ShippedQuantity.
            // Sem esta chamada o saldo da liberação de embarque não volta.
            // Depois do SaveChanges (a consulta precisa enxergar o Cancelled) e ainda
            // dentro da transação, para que uma falha aqui reverta o estorno inteiro.
            // Lido da perna de SAÍDA, a única presente nos dois caminhos: no embarque de
            // transferência é ela quem consome a liberação.
            if (sales.ShipmentReleaseKey.HasValue)
                await recalcShipped.RecalculateAsync(sales.ShipmentReleaseKey.Value);

            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e.Message, e);
            throw new ApplicationException(e.Message);
        }
        
    }
}