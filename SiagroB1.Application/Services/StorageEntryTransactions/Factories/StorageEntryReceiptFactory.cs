using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.StorageEntryTransactions.Factories;

/// <summary>
/// Converte a cópia de um romaneio de compra no romaneio de recebimento que alimenta
/// o lote de armazenagem própria. Fonte única da regra "o dono do produto no lote é
/// o CardCode do lote, não o fornecedor da compra".
/// </summary>
public static class StorageEntryReceiptFactory
{
    public static void EnsureLotAccepts(StorageAddress lot, StorageTransaction purchase)
    {
        if (lot.Status == StorageAddressStatus.Closed)
            throw new ApplicationException(
                $"Lote de armazenagem {lot.Code} está encerrado: não é possível receber.");

        if (!string.Equals(lot.ItemCode, purchase.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O produto do lote ({lot.ItemCode}) é diferente do produto da compra ({purchase.ItemCode}).");
    }

    public static void ApplyLot(StorageTransaction clone, StorageAddress lot)
    {
        clone.TransactionType = StorageTransactionType.Receipt;
        clone.TransactionStatus = StorageTransactionsStatus.Pending;

        // O produto passa a ser do lote — dono, armazém e tabela de custos vêm dele.
        clone.StorageAddressCode = lot.Code;
        clone.CardCode = lot.CardCode;
        clone.CardName = lot.CardName;
        clone.WarehouseCode = lot.WarehouseCode;
        clone.ProcessingCostCode = lot.ProcessingCostCode;

        // Quem consome a liberação é o Purchase; o Receipt não pode contar de novo.
        clone.ShipmentReleaseKey = null;

        // CalculateReceipt recalcula tudo a partir do GrossWeight, mas faz `return`
        // silencioso se o ProcessingCost do lote não existir — zerar evita herdar
        // os descontos da compra nesse caminho.
        clone.DryingDiscount = decimal.Zero;
        clone.CleaningDiscount = decimal.Zero;
        clone.OthersDicount = decimal.Zero;
        clone.AvaiableVolumeToAllocate = decimal.Zero;
    }
}
