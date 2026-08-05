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
    public async Task<ShippingTransaction> ExecuteAsync(Guid purchaseContractKey, StorageTransaction purchase, string userName)
    {
        var release = await ResolveReleaseAsync(purchase);
        var lot = await ResolveReleaseLotAsync(release, purchase);

        // Liberação de transferência de propriedade já debitou o contrato no confirm
        // da transferência — o grão foi entregue lá, não aqui. Alocar de novo debitaria
        // o dobro pelo mesmo grão. O embarque segue baixando a liberação e drenando o
        // lote; só a alocação é pulada.
        var jaAlocadoNaTransferencia = release?.Origin == ReleaseOrigin.OwnershipTransfer;

        try
        {
            await unitOfWork.BeginTransactionAsync();

            await storageCreateService.ExecuteAsync(
                purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

            await storageConfirmedService.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);

            if (!jaAlocadoNaTransferencia)
            {
                await purchaseAllocationCreateService.ExecuteAsync(
                    purchaseContractKey, purchase, purchase.NetWeight, userName, CommitMode.Deferred);
            }

            var salesCreated = await storageCopyService.ExecuteAsync(
                purchase, userName, CommitMode.Deferred);

            salesCreated.TransactionStatus = StorageTransactionsStatus.Pending;
            salesCreated.TransactionType = StorageTransactionType.SalesShipment;

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
                PurchaseStorageTransaction =  purchase,
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
            if (purchase.ShipmentReleaseKey.HasValue)
                await recalcShipped.RecalculateAsync(purchase.ShipmentReleaseKey.Value);

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

    /// <summary>
    /// Lote de onde a mercadoria vai sair, quando a liberação aponta para um.
    /// Só as liberações emitidas por transferência de titularidade têm lote: nelas o
    /// grão já está fisicamente depositado, e a saída precisa drenar aquele lote —
    /// senão o Receipt(0) gravado pela transferência vira saldo fantasma permanente.
    /// Liberação comum devolve null e o fluxo segue em nível de armazém, como sempre.
    /// </summary>
    private async Task<StorageAddress?> ResolveReleaseLotAsync(
        ShipmentRelease? release, StorageTransaction purchase)
    {
        if (release == null)
            return null;

        if (string.IsNullOrEmpty(release.StorageAddressCode))
        {
            // Integridade: uma liberação de transferência sem lote não tem como ser
            // embarcada corretamente. Só acontece com linha editada à mão.
            if (release.Origin == ReleaseOrigin.OwnershipTransfer)
                throw new ApplicationException(
                    "Liberação de transferência de propriedade sem lote de armazenagem vinculado.");

            return null;
        }

        var lot = await unitOfWork.Context.StorageAddresses
                      .AsNoTracking()
                      .FirstOrDefaultAsync(x => x.Code == release.StorageAddressCode)
                  ?? throw new ApplicationException(
                      $"Lote de armazenagem {release.StorageAddressCode} não encontrado.");

        if (!string.Equals(lot.ItemCode, purchase.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O produto do lote ({lot.ItemCode}) é diferente do produto do embarque ({purchase.ItemCode}).");

        if (balanceReader.GetBalance(lot.Code!) < purchase.GrossWeight)
            throw new ApplicationException(
                $"Saldo insuficiente no lote {lot.Code}: o produto desta liberação já foi movimentado.");

        return lot;
    }
}
