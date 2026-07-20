using SiagroB1.Application.Services.StorageEntryTransactions.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.StorageEntryTransactions;

/// <summary>
/// O Receipt do par nasce de uma cópia do romaneio de compra, mas o dono do produto
/// no módulo de armazenagem é sempre o CardCode do lote — não o fornecedor da compra.
/// Esta factory é o ponto único que faz essa troca.
/// </summary>
public class StorageEntryReceiptFactoryTests
{
    private static StorageAddress NewLot() => new()
    {
        Code = "LOTE-01",
        Description = "Lote próprio soja",
        CardCode = "C0001",
        CardName = "Yokotobi",
        ItemCode = "SOJA",
        WarehouseCode = "02",
        UoM = "KG",
        ProcessingCostCode = "PC-LOTE",
        Status = StorageAddressStatus.Open,
    };

    private static StorageTransaction NewPurchaseClone() => new()
    {
        CardCode = "F0001",
        CardName = "Fornecedor",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        ProcessingCostCode = "PC-COMPRA",
        TransactionType = StorageTransactionType.Purchase,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        GrossWeight = 1000m,
        NetWeight = 950m,
        DryingDiscount = 30m,
        CleaningDiscount = 15m,
        OthersDicount = 5m,
        ShipmentReleaseKey = Guid.NewGuid(),
        AvaiableVolumeToAllocate = 950m,
    };

    [Fact]
    public void ApplyLot_TurnsCloneIntoPendingReceipt()
    {
        var clone = NewPurchaseClone();

        StorageEntryReceiptFactory.ApplyLot(clone, NewLot());

        Assert.Equal(StorageTransactionType.Receipt, clone.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Pending, clone.TransactionStatus);
    }

    [Fact]
    public void ApplyLot_TakesOwnerAndCostsFromLot_NotFromPurchase()
    {
        var clone = NewPurchaseClone();

        StorageEntryReceiptFactory.ApplyLot(clone, NewLot());

        Assert.Equal("LOTE-01", clone.StorageAddressCode);
        Assert.Equal("C0001", clone.CardCode);
        Assert.Equal("Yokotobi", clone.CardName);
        Assert.Equal("02", clone.WarehouseCode);
        Assert.Equal("PC-LOTE", clone.ProcessingCostCode);
    }

    [Fact]
    public void ApplyLot_DetachesFromShipmentRelease()
    {
        var clone = NewPurchaseClone();

        StorageEntryReceiptFactory.ApplyLot(clone, NewLot());

        // Quem consome a liberação é o Purchase. Deixar a FK no Receipt só criaria
        // ruído no ShipmentReleaseMovementGuardService.
        Assert.Null(clone.ShipmentReleaseKey);
    }

    [Fact]
    public void ApplyLot_ZeroesInheritedDiscounts_SoConfirmRecalculatesFromLotCost()
    {
        var clone = NewPurchaseClone();

        StorageEntryReceiptFactory.ApplyLot(clone, NewLot());

        // CalculateReceipt faz `return` silencioso quando não encontra o ProcessingCost.
        // Sem zerar aqui, os descontos da compra sobreviveriam nesse caminho.
        Assert.Equal(0m, clone.DryingDiscount);
        Assert.Equal(0m, clone.CleaningDiscount);
        Assert.Equal(0m, clone.OthersDicount);
        Assert.Equal(0m, clone.AvaiableVolumeToAllocate);
    }

    [Fact]
    public void ApplyLot_KeepsGrossWeight_TheBasisForEveryDiscount()
    {
        var clone = NewPurchaseClone();

        StorageEntryReceiptFactory.ApplyLot(clone, NewLot());

        Assert.Equal(1000m, clone.GrossWeight);
    }

    [Fact]
    public void EnsureLotAccepts_RejectsLotOfAnotherItem()
    {
        var lot = NewLot();
        lot.ItemCode = "MILHO";

        var ex = Assert.Throws<ApplicationException>(
            () => StorageEntryReceiptFactory.EnsureLotAccepts(lot, NewPurchaseClone()));

        Assert.Contains("produto", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureLotAccepts_RejectsClosedLot()
    {
        var lot = NewLot();
        lot.Status = StorageAddressStatus.Closed;

        var ex = Assert.Throws<ApplicationException>(
            () => StorageEntryReceiptFactory.EnsureLotAccepts(lot, NewPurchaseClone()));

        Assert.Contains("encerrado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureLotAccepts_AcceptsOpenLotOfSameItem()
    {
        StorageEntryReceiptFactory.EnsureLotAccepts(NewLot(), NewPurchaseClone());
    }
}
