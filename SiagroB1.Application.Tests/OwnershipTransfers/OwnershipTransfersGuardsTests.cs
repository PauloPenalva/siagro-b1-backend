using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.OwnershipTransfers;

/// <summary>
/// Guardas de Confirm e Cancel. Deixaram de ser opcionais quando a confirmação
/// passou a emitir documento comercial: uma dupla confirmação duplicaria a
/// liberação de embarque e o consumo do contrato.
/// </summary>
public class OwnershipTransfersGuardsTests
{
    private readonly OwnershipTransfersTestContext _ctx = new();

    private async Task<OwnershipTransfer> SeedAsync(
        decimal quantity = 1000m,
        string transferItem = "SOJA",
        string transferUom = "KG",
        bool sameLot = false,
        StorageAddressStatus destinationStatus = StorageAddressStatus.Open,
        OwnershipTransferStatus status = OwnershipTransferStatus.Open)
    {
        var origin = OwnershipTransfersTestContext.Lot(
            "LOTE-ORIG", "P0001", StorageOwnershipType.ThirdParty);
        var destination = OwnershipTransfersTestContext.Lot(
            "LOTE-DEST", "E0001", StorageOwnershipType.OwnedInOurCustody,
            status: destinationStatus);

        var transfer = OwnershipTransfersTestContext.Transfer(
            origin, sameLot ? origin : destination, quantity, transferItem, transferUom, status);

        _ctx.Db.Context.StorageAddresses.AddRange(origin, destination);
        _ctx.Db.Context.OwnershipTransfers.Add(transfer);
        await _ctx.Db.Context.SaveChangesAsync();

        return transfer;
    }

    private async Task<OwnershipTransferStatus?> StatusOfAsync(Guid key) =>
        (await _ctx.Db.Context.OwnershipTransfers
            .AsNoTracking().SingleAsync(x => x.Key == key)).TransferStatus;

    [Fact]
    public async Task Confirm_RejectsASecondConfirmation()
    {
        var transfer = await SeedAsync();
        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_NOT_OPEN", ex.Message);

        // A movimentação de lote não pode ter sido duplicada.
        var transactions = await _ctx.Db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.OwnershipTransferKey == transfer.Key).ToListAsync();
        Assert.Equal(2, transactions.Count);
    }

    [Fact]
    public async Task Confirm_RejectsCancelledTransfer()
    {
        var transfer = await SeedAsync(status: OwnershipTransferStatus.Cancelled);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_NOT_OPEN", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsSameOriginAndDestination()
    {
        var transfer = await SeedAsync(sameLot: true);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_SAME_ADDRESS", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task Confirm_RejectsNonPositiveQuantity(decimal quantity)
    {
        var transfer = await SeedAsync(quantity: quantity);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_INVALID_QUANTITY", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsItemDifferentFromLot()
    {
        var transfer = await SeedAsync(transferItem: "MILHO");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_ITEM_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsUomDifferentFromLot()
    {
        var transfer = await SeedAsync(transferUom: "TON");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_UOM_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsClosedLot()
    {
        var transfer = await SeedAsync(destinationStatus: StorageAddressStatus.Closed);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_LOT_CLOSED", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectionLeavesNothingPersisted()
    {
        var transfer = await SeedAsync(sameLot: true);

        await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));

        Assert.Empty(await _ctx.Db.Context.StorageTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(OwnershipTransferStatus.Open, await StatusOfAsync(transfer.Key));
    }

    [Fact]
    public async Task Cancel_OfAnOpenTransferJustFlipsTheStatus()
    {
        var transfer = await SeedAsync();

        await _ctx.Cancel().ExecuteAsync(transfer.Key, "tester");

        Assert.Equal(OwnershipTransferStatus.Cancelled, await StatusOfAsync(transfer.Key));
        Assert.Empty(await _ctx.Db.Context.StorageTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Cancel_OfAClosedTransferWritesTheReversingTransactions()
    {
        var transfer = await SeedAsync(quantity: 600m);
        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        await _ctx.Cancel().ExecuteAsync(transfer.Key, "tester");

        Assert.Equal(OwnershipTransferStatus.Cancelled, await StatusOfAsync(transfer.Key));

        var transactions = await _ctx.Db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.OwnershipTransferKey == transfer.Key).ToListAsync();

        // Duas da confirmação + duas compensatórias.
        Assert.Equal(4, transactions.Count);
        Assert.Equal(2, transactions.Count(x => x.StorageAddressCode == "LOTE-ORIG"));
        Assert.Equal(2, transactions.Count(x => x.StorageAddressCode == "LOTE-DEST"));
    }

    [Fact]
    public async Task Cancel_RejectsASecondCancellation()
    {
        var transfer = await SeedAsync();
        await _ctx.Cancel().ExecuteAsync(transfer.Key, "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_ALREADY_CANCELLED", ex.Message);
    }
}
