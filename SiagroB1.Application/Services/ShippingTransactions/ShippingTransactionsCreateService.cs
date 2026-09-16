using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShippingTransactions;

/// <summary>
/// Expedição de Grãos. Cria o par Purchase/SalesShipment: o Purchase baixa contrato e
/// liberação de embarque, a cópia retipada dá saída no armazém.
/// <para>
/// <b>Exceção — liberações cujo físico JÁ está em nosso poder</b>
/// (<see cref="ReleaseOriginRules.ShipsWithoutPurchaseLeg"/>). Nelas a entrada no armazém já
/// aconteceu antes:
/// </para>
/// <list type="bullet">
/// <item><see cref="ReleaseOrigin.OwnershipTransfer"/> — o confirm da transferência criou o
/// Purchase(8), alocou o contrato e creditou o armazém.</item>
/// <item><see cref="ReleaseOrigin.SalesReturn"/> — a devolução criou o
/// <see cref="StorageTransactionType.SalesShipmentReturn"/>, que creditou o armazém; o
/// contrato já havia sido debitado quando a mercadoria saiu pela primeira vez.</item>
/// </list>
/// <para>
/// Criar outro Purchase(8) aqui creditaria o armazém uma segunda vez por grão que está
/// SAINDO — saldo fantasma — e alocaria o contrato de novo. Nesses caminhos a Expedição cria
/// <b>só</b> a perna de saída, que debita o armazém (drenando o lote quando houver) e consome
/// a liberação, e não pede contrato de compra ao usuário.
/// </para>
/// </summary>
public class ShippingTransactionsCreateService(
    IUnitOfWork unitOfWork,
    StorageTransactionsCreateService storageCreateService,
    StorageTransactionsConfirmedService storageConfirmedService,
    StorageTransactionsCopyService storageCopyService,
    PurchaseContractsAllocationCreateService purchaseAllocationCreateService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    IStorageAddressBalanceReader balanceReader)
{
    public async Task<ShippingTransaction> ExecuteAsync(Guid? purchaseContractKey, StorageTransaction purchase, string userName)
    {
        var release = await ResolveReleaseAsync(purchase);
        var lot = await ShipmentReleaseLotRules.ResolveAsync(
            unitOfWork.Context, balanceReader, release, purchase.ItemCode, purchase.WarehouseCode, purchase.GrossWeight);

        // Transferência de titularidade e devolução ao armazém já registraram a entrada do
        // grão. Aqui só resta a saída. Ver o <summary> da classe.
        // Romaneio SEM liberação (release == null) segue exigindo contrato, como sempre.
        var embarqueSemPernaDeCompra =
            release != null && ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin);

        if (!embarqueSemPernaDeCompra && !purchaseContractKey.HasValue)
            throw new ApplicationException("Contrato de compra é obrigatório para este embarque.");

        try
        {
            await unitOfWork.BeginTransactionAsync();

            StorageTransaction salesCreated;

            if (embarqueSemPernaDeCompra)
            {
                // Sem perna de compra não há original de onde copiar: a saída é montada
                // direto do payload da tela, que já traz produto, peso, armazém e a
                // liberação. ShipmentLoadKey/SalesInvoiceKey ficam nulos — a carga e o
                // faturamento são passos posteriores.
                salesCreated = purchase;
                salesCreated.TransactionType = StorageTransactionType.SalesShipment;
                // Pending é pré-condição do confirm de SalesShipment; no caminho normal
                // quem carimba isso é a cópia.
                salesCreated.TransactionStatus = StorageTransactionsStatus.Pending;
                salesCreated.ShipmentLoadKey = null;
                salesCreated.SalesInvoiceKey = null;

                await storageCreateService.ExecuteAsync(
                    salesCreated, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
            }
            else
            {
                await storageCreateService.ExecuteAsync(
                    purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

                await storageConfirmedService.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);

                await purchaseAllocationCreateService.ExecuteAsync(
                    purchaseContractKey!.Value, purchase, purchase.NetWeight, userName, CommitMode.Deferred);

                salesCreated = await storageCopyService.ExecuteAsync(
                    purchase, userName, CommitMode.Deferred);

                salesCreated.TransactionStatus = StorageTransactionsStatus.Pending;
                salesCreated.TransactionType = StorageTransactionType.SalesShipment;
            }

            // Só a perna de SAÍDA carrega o lote. O SalesShipment(7) está no conjunto de
            // saída do saldo do lote; o Purchase(8) não entra em nenhuma das duas pontas
            // dessa fórmula e é a perna comercial — deixá-lo com o lote só sujaria o
            // extrato. A atribuição vem DEPOIS da cópia porque
            // StorageTransactionCopyFactory copia StorageAddressCode do original.
            if (lot != null)
                salesCreated.StorageAddressCode = lot.Code;

            await storageConfirmedService.ExecuteAsync(salesCreated, userName, CommitMode.Deferred, true);

            var shipping = new ShippingTransaction
            {
                PurchaseStorageTransaction = embarqueSemPernaDeCompra ? null : purchase,
                SalesStorageTransaction = salesCreated,
            };

            await unitOfWork.Context.ShippingTransactions.AddAsync(shipping);

            await unitOfWork.SaveChangesAsync();

            await unitOfWork.CommitAsync();

            // Fora da transação e explícito: os hooks de ShippedQuantity em
            // Create/Confirmed só disparam em CommitMode.Auto, e todo o fluxo acima
            // roda em Deferred — sem esta chamada a liberação não seria atualizada.
            // Só aqui, depois do commit: a cópia de venda nasce tipada como Purchase e
            // só é retipada para SalesShipment em memória, então um recálculo antecipado
            // contaria o par inteiro e dobraria o volume romaneado.
            if (salesCreated.ShipmentReleaseKey.HasValue)
                await recalcShipped.RecalculateAsync(salesCreated.ShipmentReleaseKey.Value);

            return shipping;
        }
        catch (Exception e)
        {
            await unitOfWork.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }

    /// <summary>Liberação que está sendo embarcada, quando o romaneio aponta para uma.</summary>
    private async Task<ShipmentRelease?> ResolveReleaseAsync(StorageTransaction purchase)
    {
        if (!purchase.ShipmentReleaseKey.HasValue)
            return null;

        return await unitOfWork.Context.ShipmentReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == purchase.ShipmentReleaseKey.Value);
    }
}
