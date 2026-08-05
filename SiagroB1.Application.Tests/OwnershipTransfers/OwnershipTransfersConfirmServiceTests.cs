using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.OwnershipTransfers;

/// <summary>
/// Comportamento de custódia da confirmação: o par Shipment(1) na origem /
/// Receipt(0) no destino. É o que a transferência já fazia antes de passar a
/// emitir documento comercial — estes testes existem para que o acréscimo do
/// contrato não altere silenciosamente a movimentação de lote.
/// </summary>
public class OwnershipTransfersConfirmServiceTests
{
    private readonly OwnershipTransfersTestContext _ctx = new();

    private async Task<OwnershipTransfer> SeedAsync(decimal quantity = 1000m)
    {
        var origin = OwnershipTransfersTestContext.Lot(
            "LOTE-ORIG", "P0001", StorageOwnershipType.ThirdParty);
        var destination = OwnershipTransfersTestContext.Lot(
            "LOTE-DEST", "E0001", StorageOwnershipType.OwnedInOurCustody);
        var transfer = OwnershipTransfersTestContext.Transfer(origin, destination, quantity);

        _ctx.Db.Context.StorageAddresses.AddRange(origin, destination);
        _ctx.Db.Context.OwnershipTransfers.Add(transfer);
        await _ctx.Db.Context.SaveChangesAsync();

        return transfer;
    }

    [Fact]
    public async Task Confirm_ClosesTheTransferAndStampsApproval()
    {
        var transfer = await SeedAsync();

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var reloaded = await _ctx.Db.Context.OwnershipTransfers
            .AsNoTracking().SingleAsync(x => x.Key == transfer.Key);

        Assert.Equal(OwnershipTransferStatus.Closed, reloaded.TransferStatus);
        Assert.Equal("tester", reloaded.ApprovedBy);
        Assert.NotNull(reloaded.ApprovedAt);
    }

    [Fact]
    public async Task Confirm_WritesShipmentOnOriginAndReceiptOnDestination()
    {
        var transfer = await SeedAsync(quantity: 750m);

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var transactions = await _ctx.Db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.OwnershipTransferKey == transfer.Key).ToListAsync();

        Assert.Equal(2, transactions.Count);

        var origin = Assert.Single(transactions, x => x.StorageAddressCode == "LOTE-ORIG");
        Assert.Equal(StorageTransactionType.Shipment, origin.TransactionType);
        Assert.Equal(750m, origin.NetWeight);

        var destination = Assert.Single(transactions, x => x.StorageAddressCode == "LOTE-DEST");
        Assert.Equal(StorageTransactionType.Receipt, destination.TransactionType);
        Assert.Equal(750m, destination.NetWeight);
    }

    [Fact]
    public async Task Confirm_MarksBothTransactionsAsOwnershipTransferAndNotAllocatable()
    {
        var transfer = await SeedAsync();

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var transactions = await _ctx.Db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.OwnershipTransferKey == transfer.Key).ToListAsync();

        Assert.All(transactions, t =>
        {
            Assert.Equal("Y", t.IsOwnershipTransfer);
            Assert.Equal(TransactionCode.OwnershipTransfer, t.TransactionOrigin);
            Assert.Equal(StorageTransactionsStatus.Confirmed, t.TransactionStatus);
            // Um romaneio de transferência nunca é alocável a contrato: quem aloca
            // é o Purchase(8) gerado mais tarde pela Expedição de Grãos.
            Assert.Equal(decimal.Zero, t.AvaiableVolumeToAllocate);
        });
    }

    [Fact]
    public async Task Confirm_BlocksWhenOriginLotHasNotEnoughBalance()
    {
        var transfer = await SeedAsync(quantity: 1000m);

        await Assert.ThrowsAnyAsync<Exception>(
            () => _ctx.Confirm(lotBalance: 400m).ExecuteAsync(transfer.Key, "tester"));

        var reloaded = await _ctx.Db.Context.OwnershipTransfers
            .AsNoTracking().SingleAsync(x => x.Key == transfer.Key);
        Assert.Equal(OwnershipTransferStatus.Open, reloaded.TransferStatus);
    }
}
