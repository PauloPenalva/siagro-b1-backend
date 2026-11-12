using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Recusa com destino Quebra de trajeto (GAC-1213): a mercadoria se perdeu no caminho (avaria,
/// sinistro, roubo). As devoluções devolvem o saldo do contrato de venda, nasce um romaneio tipo
/// 18 só de registro e a carga fecha em "Quebra de trajeto" — sem armazém creditado, sem
/// liberação nova e sem mexer na perna de compra.
/// </summary>
/// <remarks>
/// Reaproveita a fixture de <see cref="ShipmentLoadsRefuseServiceTests"/>, que monta e fatura a
/// carga pelo caminho REAL, como <see cref="ShipmentLoadsRefuseTransshipmentTests"/>.
/// </remarks>
public class ShipmentLoadsRefuseTransitLossTests
{
    private const string Reason = "Carga roubada na BR-116";

    private readonly ShipmentLoadsRefuseServiceTests _fixture = new();

    private static RefusalRequest TransitLossRequest(
        ShipmentLoad load, SalesInvoice invoice, decimal quantity,
        TransitLossReason? reason = TransitLossReason.Theft, string? warehouseCode = null) =>
        new(load.Key,
            [new RefusalLine(invoice.Key, quantity)],
            RefusalDestination.TransitLoss,
            warehouseCode,
            Reason,
            reason);

    /// <summary>
    /// A perna de compra da carga: romaneio Compra (8) faturado, alocado no contrato de compra.
    /// A quebra de trajeto não pode tocar em nada disso — o grão foi comprado e entregue.
    /// </summary>
    private async Task<(PurchaseContract Contract, StorageTransaction Purchase)> SeedPurchaseLegAsync(decimal quantity)
    {
        var db = _fixture._db.Context;

        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-ORIG",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = ShipmentLoadsRefuseServiceTests.OriginWarehouse,
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
            AllocatedVolume = quantity,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = ShipmentLoadsRefuseServiceTests.OriginWarehouse,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };

        var purchase = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RC1",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = ShipmentLoadsRefuseServiceTests.OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = quantity,
            NetWeight = quantity,
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Invoiced,
            ShipmentReleaseKey = release.Key,
        };

        db.PurchaseContracts.Add(contract);
        db.ShipmentReleases.Add(release);
        db.StorageTransactions.Add(purchase);
        db.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            PurchaseContractKey = contract.Key,
            StorageTransactionKey = purchase.Key,
            Volume = quantity,
        });

        await _fixture._db.SaveChangesAsync();
        return (contract, purchase);
    }

    private async Task<(ShipmentLoad Load, SalesInvoice Invoice, SalesContract Contract, SalesShipmentRelease Release)>
        BilledLoadAsync(decimal quantity = 40_000m)
    {
        var (load, contract, release, _) = await _fixture.SeedAsync(quantity);
        var invoice = ShipmentLoadsRefuseServiceTests.InvoiceFor(load, contract, release, quantity);
        await _fixture.BillingService().ExecuteAsync(invoice, "tester");
        return (load, invoice, contract, release);
    }

    private Task<List<StorageTransaction>> TransitLossEntriesAsync() =>
        _fixture._db.Context.StorageTransactions.AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.TransitLoss)
            .ToListAsync();

    [Fact]
    public async Task TotalLoss_ClosesLoadAndKeepsPurchaseDelivered()
    {
        var db = _fixture._db.Context;
        var (load, invoice, salesContract, salesRelease) = await BilledLoadAsync();
        var (purchaseContract, purchase) = await SeedPurchaseLegAsync(40_000m);

        var purchaseAllocatedBefore = (await db.PurchaseContracts.AsNoTracking()
            .SingleAsync(x => x.Key == purchaseContract.Key)).AllocatedVolume;
        var purchaseAllocationsBefore = await db.PurchaseContractsAllocations.AsNoTracking()
            .Where(x => x.PurchaseContractKey == purchaseContract.Key).SumAsync(x => x.Volume);
        var salesAllocatedBefore = (await SalesContractsAllocationTestSupport
            .ContractAsync(_fixture._db, salesContract.Key)).AllocatedVolume;
        var releasesBefore = await db.ShipmentReleases.CountAsync();
        var originBalanceBefore = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            db, ShipmentLoadsRefuseServiceTests.OriginWarehouse, "SOJA");
        Assert.Equal(40_000m, salesAllocatedBefore);

        await _fixture.Service().ExecuteAsync(TransitLossRequest(load, invoice, 40_000m), "tester");

        // Devolução confirmada, como em todo destino.
        var returnInvoice = await db.SalesInvoices.AsNoTracking()
            .SingleAsync(x => x.InvoiceType == SalesInvoiceType.Return);
        Assert.Equal(InvoiceStatus.Confirmed, returnInvoice.InvoiceStatus);

        // O romaneio 18.
        var entry = Assert.Single(await TransitLossEntriesAsync());
        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Equal(40_000m, entry.GrossWeight);
        Assert.Equal(TransitLossReason.Theft, entry.TransitLossReason);
        Assert.Equal(load.Key, entry.RefusedFromShipmentLoadKey);
        Assert.Null(entry.ShipmentLoadKey);
        Assert.Null(entry.ShipmentReleaseKey);
        Assert.Null(entry.ReturnInvoiceKey);
        Assert.Equal(load.WarehouseCode, entry.WarehouseCode);
        Assert.Contains($"Documento(s): {invoice.InvoiceNumber}", entry.Comments);
        Assert.Contains("Roubo de carga", entry.Comments);
        Assert.Contains(Reason, entry.Comments);
        Assert.Equal(new HashSet<string> { invoice.InvoiceNumber! }, RefusalEntryDocuments.Parse(entry.Comments));

        // A carga.
        var closed = await _fixture.LoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.TransitLoss, closed.Status);
        Assert.Equal(40_000m, closed.TransitLossQuantity);
        Assert.Equal(decimal.Zero, closed.AvailableQuantity);
        Assert.Equal(decimal.Zero, closed.ReturnedToWarehouseQuantity);

        var movement = await db.ShipmentLoadMovements.AsNoTracking()
            .SingleAsync(x => x.ShipmentLoadKey == load.Key &&
                              x.MovementType == ShipmentLoadMovementType.TransitLoss);
        Assert.Equal(-40_000m, movement.Quantity);
        Assert.Equal(decimal.Zero, movement.BalanceAfter);
        Assert.Equal(entry.Key, movement.StorageTransactionKey);
        Assert.Equal(Reason, movement.Reason);

        // A perna de compra intacta.
        var purchaseAfter = await db.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == purchase.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, purchaseAfter.TransactionStatus);
        Assert.Equal(purchaseAllocatedBefore, (await db.PurchaseContracts.AsNoTracking()
            .SingleAsync(x => x.Key == purchaseContract.Key)).AllocatedVolume);
        Assert.Equal(purchaseAllocationsBefore, await db.PurchaseContractsAllocations.AsNoTracking()
            .Where(x => x.PurchaseContractKey == purchaseContract.Key).SumAsync(x => x.Volume));

        // Saldo de venda restaurado.
        Assert.Equal(decimal.Zero, (await SalesContractsAllocationTestSupport
            .ContractAsync(_fixture._db, salesContract.Key)).AllocatedVolume);
        Assert.Equal(decimal.Zero, (await SalesContractsAllocationTestSupport
            .ReleaseAsync(_fixture._db, salesRelease.Key)).ShippedQuantity);

        // Nenhuma liberação nova, nenhum armazém mexido.
        Assert.Equal(releasesBefore, await db.ShipmentReleases.CountAsync());
        Assert.Equal(originBalanceBefore, await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            db, ShipmentLoadsRefuseServiceTests.OriginWarehouse, "SOJA"));
    }

    [Fact]
    public async Task PartialLoss_KeepsRestDelivered()
    {
        var (load, invoice, _, _) = await BilledLoadAsync();

        await _fixture.Service().ExecuteAsync(TransitLossRequest(load, invoice, 15_000m), "tester");

        var refused = await _fixture.LoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.TransitLoss, refused.Status);
        Assert.Equal(25_000m, refused.InvoicedQuantity);
        Assert.Equal(15_000m, refused.TransitLossQuantity);
        Assert.Equal(decimal.Zero, refused.AvailableQuantity);
    }

    [Fact]
    public async Task PartialLossThenWarehouseReturn()
    {
        var (load, invoice, _, _) = await BilledLoadAsync();

        await _fixture.Service().ExecuteAsync(TransitLossRequest(load, invoice, 15_000m), "tester");
        await _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, 25_000m, RefusalDestination.Warehouse,
                ShipmentLoadsRefuseServiceTests.DestinationWarehouse),
            "tester");

        var refused = await _fixture.LoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.TransitLoss, refused.Status);
        Assert.Equal(15_000m, refused.TransitLossQuantity);
        Assert.Equal(25_000m, refused.ReturnedToWarehouseQuantity);
        Assert.Equal(decimal.Zero, refused.AvailableQuantity);
    }

    [Fact]
    public async Task MissingReason_IsRejectedBeforeAnyWrite()
    {
        var (load, invoice, _, _) = await BilledLoadAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => _fixture.Service()
            .ExecuteAsync(TransitLossRequest(load, invoice, 40_000m, reason: null), "tester"));

        Assert.Equal("Informe o tipo da quebra de trajeto.", error.Message);
        Assert.False(await _fixture._db.Context.SalesInvoices.AnyAsync(x => x.InvoiceType == SalesInvoiceType.Return));
        Assert.Empty(await TransitLossEntriesAsync());
    }

    [Fact]
    public async Task WarehouseInformed_IsRejected()
    {
        var (load, invoice, _, _) = await BilledLoadAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => _fixture.Service()
            .ExecuteAsync(TransitLossRequest(load, invoice, 40_000m,
                warehouseCode: ShipmentLoadsRefuseServiceTests.DestinationWarehouse), "tester"));

        Assert.Equal("A quebra de trajeto não tem armazém de destino.", error.Message);
        Assert.False(await _fixture._db.Context.SalesInvoices.AnyAsync(x => x.InvoiceType == SalesInvoiceType.Return));
        Assert.Empty(await TransitLossEntriesAsync());
    }

    [Fact]
    public async Task TransitLossReasonOnOtherDestination_IsIgnored()
    {
        var (load, invoice, _, _) = await BilledLoadAsync();

        await _fixture.Service().ExecuteAsync(
            new RefusalRequest(load.Key, [new RefusalLine(invoice.Key, 40_000m)],
                RefusalDestination.Rebilling, null, Reason, TransitLossReason.Damage),
            "tester");

        Assert.Empty(await TransitLossEntriesAsync());
        var refused = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, refused.TransitLossQuantity);
        Assert.Equal(40_000m, refused.AvailableQuantity);
    }
}
