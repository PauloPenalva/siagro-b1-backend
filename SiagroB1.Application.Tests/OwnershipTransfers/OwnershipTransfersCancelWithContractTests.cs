using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.OwnershipTransfers;

/// <summary>
/// Cancelar a transferência precisa desfazer os dois lados: a custódia (romaneios
/// compensatórios de lote) e o comercial (a liberação de embarque emitida).
/// </summary>
public class OwnershipTransfersCancelWithContractTests
{
    private readonly OwnershipTransfersTestContext _ctx = new();

    private async Task<(OwnershipTransfer Transfer, PurchaseContract Contract)> SeedConfirmedAsync(
        decimal quantity = 1000m)
    {
        var origin = OwnershipTransfersTestContext.Lot(
            "LOTE-ORIG", "P0001", StorageOwnershipType.ThirdParty);
        var destination = OwnershipTransfersTestContext.Lot(
            "LOTE-DEST", "E0001", StorageOwnershipType.OwnedInOurCustody);
        var contract = OwnershipTransfersTestContext.Contract();

        var transfer = OwnershipTransfersTestContext.Transfer(origin, destination, quantity);
        transfer.PurchaseContractKey = contract.Key;

        _ctx.Db.Context.StorageAddresses.AddRange(origin, destination);
        _ctx.Db.Context.PurchaseContracts.Add(contract);
        _ctx.Db.Context.OwnershipTransfers.Add(transfer);
        await _ctx.Db.Context.SaveChangesAsync();

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        return (transfer, contract);
    }

    private Task<ShipmentRelease> ReleaseOfAsync(Guid transferKey) =>
        _ctx.Db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.OwnershipTransferKey == transferKey);

    /// <summary>Simula um embarque contra a liberação (o Purchase(8) da Expedição).</summary>
    private async Task ShipAsync(Guid releaseKey, decimal netWeight)
    {
        _ctx.Db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = netWeight,
            GrossWeight = netWeight,
            ShipmentReleaseKey = releaseKey,
        });
        await _ctx.Db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Cancel_CancelsTheGeneratedRelease()
    {
        var (transfer, _) = await SeedConfirmedAsync();

        await _ctx.Cancel().ExecuteAsync(transfer.Key, "tester");

        var release = await ReleaseOfAsync(transfer.Key);
        Assert.Equal(ReleaseStatus.Cancelled, release.Status);
        Assert.Contains("OT-0001", release.CancellationReason);
    }

    [Fact]
    public async Task Cancel_ReturnsTheReleaseBalanceToTheContract()
    {
        var (transfer, contract) = await SeedConfirmedAsync(quantity: 1000m);

        await _ctx.Cancel().ExecuteAsync(transfer.Key, "tester");

        var reloaded = await _ctx.Db.Context.PurchaseContracts
            .AsNoTracking().Include(x => x.ShipmentReleases)
            .SingleAsync(x => x.Key == contract.Key);

        // Nada foi romaneado, então a liberação cancelada consome zero.
        Assert.Equal(10_000m, reloaded.TotalAvailableToRelease);
    }

    [Fact]
    public async Task Cancel_BlocksWhenTheReleaseAlreadyHasShipments()
    {
        var (transfer, _) = await SeedConfirmedAsync(quantity: 1000m);
        var release = await ReleaseOfAsync(transfer.Key);
        await ShipAsync(release.Key, 400m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_RELEASE_ALREADY_SHIPPED", ex.Message);

        // Nada pode ter mudado.
        var reloadedTransfer = await _ctx.Db.Context.OwnershipTransfers
            .AsNoTracking().SingleAsync(x => x.Key == transfer.Key);
        Assert.Equal(OwnershipTransferStatus.Closed, reloadedTransfer.TransferStatus);
        Assert.Equal(ReleaseStatus.Actived, (await ReleaseOfAsync(transfer.Key)).Status);
    }

    [Fact]
    public async Task Cancel_WithoutContract_StillWorks()
    {
        var origin = OwnershipTransfersTestContext.Lot(
            "LOTE-ORIG", "P0001", StorageOwnershipType.ThirdParty);
        var destination = OwnershipTransfersTestContext.Lot(
            "LOTE-DEST", "E0001", StorageOwnershipType.OwnedInOurCustody);
        var transfer = OwnershipTransfersTestContext.Transfer(origin, destination);

        _ctx.Db.Context.StorageAddresses.AddRange(origin, destination);
        _ctx.Db.Context.OwnershipTransfers.Add(transfer);
        await _ctx.Db.Context.SaveChangesAsync();
        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        await _ctx.Cancel().ExecuteAsync(transfer.Key, "tester");

        var reloaded = await _ctx.Db.Context.OwnershipTransfers
            .AsNoTracking().SingleAsync(x => x.Key == transfer.Key);
        Assert.Equal(OwnershipTransferStatus.Cancelled, reloaded.TransferStatus);
    }

    [Fact]
    public async Task ReleaseServices_RefuseToActOnAnOwnershipTransferRelease()
    {
        var (transfer, _) = await SeedConfirmedAsync();
        var release = await ReleaseOfAsync(transfer.Key);
        var recalc = new ShipmentReleasesRecalculateShippedService(_ctx.Db.Context);

        var cancelation = new ShipmentReleasesCancelationService(
            _ctx.Db.Context, recalc, NullLogger<ShipmentReleasesCancelationService>.Instance);
        var cancelEx = await Assert.ThrowsAsync<ApplicationException>(
            () => cancelation.ExecuteAsync(release.Key, "tester", "motivo"));
        Assert.Contains("transferência de propriedade", cancelEx.Message);

        var pause = new ShipmentReleasesPauseService(
            _ctx.Db.Context, NullLogger<ShipmentReleasesCancelationService>.Instance);
        var pauseEx = await Assert.ThrowsAsync<ApplicationException>(
            () => pause.ExecuteAsync(release.Key));
        Assert.Contains("transferência de propriedade", pauseEx.Message);

        var close = new ShipmentReleasesCloseService(_ctx.Db.Context);
        var closeEx = await Assert.ThrowsAsync<ApplicationException>(
            () => close.ExecuteAsync(release.Key, "tester"));
        Assert.Contains("transferência de propriedade", closeEx.Message);
    }
}
