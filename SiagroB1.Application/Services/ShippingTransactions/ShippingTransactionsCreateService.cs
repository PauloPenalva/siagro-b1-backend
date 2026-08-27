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
/// <b>Exceção — liberação emitida por transferência de titularidade.</b> Ali a compra já
/// aconteceu: o confirm da transferência criou o Purchase(8), alocou o contrato e creditou
/// o armazém. Criar outro Purchase(8) aqui creditaria o armazém uma segunda vez por grão
/// que está saindo, deixando saldo fantasma. Nesse caminho a Expedição cria <b>só</b> a
/// perna de saída — que drena o lote, debita o armazém e consome a liberação — e não pede
/// contrato de compra ao usuário.
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
        var lot = await ResolveReleaseLotAsync(release, purchase);

        // A transferência de titularidade já registrou a compra: alocou o contrato e
        // creditou o armazém. Aqui só resta a saída. Ver o <summary> da classe.
        var embarqueDeTransferencia = release?.Origin == ReleaseOrigin.OwnershipTransfer;

        if (!embarqueDeTransferencia && !purchaseContractKey.HasValue)
            throw new ApplicationException("Contrato de compra é obrigatório para este embarque.");

        try
        {
            await unitOfWork.BeginTransactionAsync();

            StorageTransaction salesCreated;

            if (embarqueDeTransferencia)
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
                PurchaseStorageTransaction = embarqueDeTransferencia ? null : purchase,
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

        // O armazém vem do payload da tela e é ele que o saldo de armazém usa. Divergindo
        // do armazém do lote, a saída debitaria um armazém e a entrada gravada pela
        // transferência ficaria presa no outro — dois saldos errados de uma vez.
        if (!string.Equals(lot.WarehouseCode, purchase.WarehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O armazém do embarque ({purchase.WarehouseCode}) é diferente do armazém do lote {lot.Code} ({lot.WarehouseCode}).");

        return lot;
    }
}
