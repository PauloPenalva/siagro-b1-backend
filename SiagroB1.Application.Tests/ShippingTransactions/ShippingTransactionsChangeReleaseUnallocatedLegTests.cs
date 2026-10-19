using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShippingTransactions;

/// <summary>
/// Trocar liberação com a perna de compra (8) desalocada — o estado da CEAGUI, onde as pernas 8
/// das Expedições de Grãos foram desalocadas à mão (spec 2026-10-19, decisão 8). O estorno 9 só
/// devolve o que a 8 original tinha alocado, contrato a contrato; a 8 nova é alocada no destino
/// como sempre.
/// </summary>
public class ShippingTransactionsChangeReleaseUnallocatedLegTests
{
    private static readonly DateTime LoadDate = new(2026, 9, 10);
    private const decimal Net = 50_000m;

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeDocNumberSequenceService _docNumbers = new();

    private ShipmentReleasesRecalculateShippedService Recalc() => new(_db.Context);

    private StorageTransactionsGetService GetService() =>
        new(_db, NullLogger<StorageTransactionsGetService>.Instance);

    private StorageTransactionsCreateService StorageCreate() =>
        new(_db,
            _docNumbers,
            new FakeBusinessPartnerService(new() { ["F0001"] = "Produtor Um", ["F0002"] = "Produtor Dois" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["01"] = "Armazém 01", ["02"] = "Armazém 02" }),
            Recalc(),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirmed() =>
        new(_db, new FakeStringLocalizer<Resource>(), Recalc(),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private ShippingTransactionsCreateService CreateService()
    {
        var storageCreate = StorageCreate();
        return new ShippingTransactionsCreateService(
            _db, storageCreate, StorageConfirmed(),
            new StorageTransactionsCopyService(_db, _docNumbers, storageCreate, new FakeStringLocalizer<Resource>()),
            new PurchaseContractsAllocationCreateService(_db, GetService()),
            Recalc(),
            new FakeStorageAddressBalanceReader(1_000_000m));
    }

    private ShippingTransactionsChangeReleaseService Service() =>
        new(_db,
            StorageCreate(),
            StorageConfirmed(),
            new PurchaseContractsAllocationCreateService(_db, GetService()),
            Recalc(),
            new ShipmentLoadsMovementLogService(_db.Context),
            new ShippingReleaseChangeReconciliationGuard(_db.Context),
            new FakeStorageAddressBalanceReader(1_000_000m));

    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedReleaseAsync(
        string contractCode, string cardCode, string warehouseCode = "01")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = contractCode,
            CardCode = cardCode,
            CardName = cardCode == "F0001" ? "Produtor Um" : "Produtor Dois",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouseCode,
            Status = ContractStatus.Approved,
            TotalVolume = 100_000m,
            AllocatedVolume = 0m,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = warehouseCode,
            DeliveryLocationName = $"Armazém {warehouseCode}",
            ReleasedQuantity = 60_000m,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.Standard,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();
        return (contract, release);
    }

    /// <summary>Expedição de Grãos real (8 alocada no contrato da liberação) numa carga faturada.</summary>
    private async Task<ShippingTransaction> ShipIntoInvoicedLoadAsync(PurchaseContract contract, ShipmentRelease release)
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = contract.CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Pending,
            GrossWeight = Net,
            ShipmentReleaseKey = release.Key,
            TransactionTime = "07:15:00",
        };

        var shipping = await CreateService().ExecuteAsync(contract.Key, st, "tester");

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "01",
            LoadDate = LoadDate,
            Status = ShipmentLoadStatus.Open,
            TotalQuantity = Net,
        };
        _db.Context.ShipmentLoads.Add(load);

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000123",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
        };
        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = Net,
        });
        _db.Context.SalesInvoices.Add(invoice);

        var sales = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipping.SalesStorageTransactionKey);
        sales.ShipmentLoadKey = load.Key;
        sales.SalesInvoiceKey = invoice.Key;
        sales.TransactionStatus = StorageTransactionsStatus.Invoiced;
        sales.IsInvoiced = true;
        sales.InvoicedAt = LoadDate.AddHours(10);
        sales.InvoiceQty = Net;
        await _db.Context.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, null);
        await _db.Context.SaveChangesAsync();

        return shipping;
    }

    /// <summary>
    /// Reescreve as alocações da 8 como a CEAGUI deixou: <paramref name="allocations"/> vazio é a
    /// perna desalocada à mão. O AllocatedVolume dos contratos e o saldo da 8 acompanham.
    /// </summary>
    private async Task ReplaceLegAllocationsAsync(
        Guid purchaseKey, params (PurchaseContract Contract, decimal Volume)[] allocations)
    {
        var rows = await _db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == purchaseKey)
            .ToListAsync();
        _db.Context.PurchaseContractsAllocations.RemoveRange(rows);

        foreach (var (contract, volume) in allocations)
        {
            _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
                StorageTransactionKey = purchaseKey,
                Volume = volume,
                ApprovedAt = DateTime.Now,
                ApprovedBy = "tester",
            });
        }

        var purchase = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == purchaseKey);
        purchase.AvaiableVolumeToAllocate = purchase.NetWeight - allocations.Sum(x => x.Volume);
        await _db.Context.SaveChangesAsync();

        foreach (var contract in await _db.Context.PurchaseContracts.ToListAsync())
        {
            contract.AllocatedVolume = await _db.Context.PurchaseContractsAllocations
                .Where(x => x.PurchaseContractKey == contract.Key)
                .SumAsync(x => x.Volume);
        }
        await _db.Context.SaveChangesAsync();
    }

    private Task<StorageTransaction> Tx(Guid key) =>
        _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<PurchaseContract> ContractOf(Guid key) =>
        _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<ShippingReleaseChange> ChangeOf(Guid originalSalesKey) =>
        _db.Context.ShippingReleaseChanges.AsNoTracking()
            .SingleAsync(x => x.OriginalSalesStorageTransactionKey == originalSalesKey);

    private Task<List<PurchaseContractAllocation>> AllocationsOf(Guid storageTransactionKey) =>
        _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .Where(x => x.StorageTransactionKey == storageTransactionKey)
            .ToListAsync();

    [Fact]
    public async Task UnallocatedLeg_ReversalDoesNotTouchAnyContract()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var shipping = await ShipIntoInvoicedLoadAsync(c1, r1);
        var purchaseKey = shipping.PurchaseStorageTransactionKey!.Value;
        await ReplaceLegAllocationsAsync(purchaseKey);
        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume); // cenário CEAGUI

        await Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "Contrato certo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);

        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(Net, (await ContractOf(c2.Key)).AllocatedVolume);

        var return9 = await Tx(change.ReturnPurchaseStorageTransactionKey!.Value);
        Assert.Empty(await AllocationsOf(return9.Key));
        Assert.Equal(0m, return9.AvaiableVolumeToAllocate);
        Assert.Equal(0m, (await Tx(purchaseKey)).AvaiableVolumeToAllocate);

        var newPurchase = await Tx(change.NewPurchaseStorageTransactionKey!.Value);
        var allocation = Assert.Single(await AllocationsOf(newPurchase.Key));
        Assert.Equal(c2.Key, allocation.PurchaseContractKey);
        Assert.Equal(Net, allocation.Volume);
    }

    [Fact]
    public async Task PartiallyAllocatedLeg_ReversesOnlyAllocatedPerContract()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var (c3, _) = await SeedReleaseAsync("PC-003", "F0001");
        var shipping = await ShipIntoInvoicedLoadAsync(c1, r1);
        var purchaseKey = shipping.PurchaseStorageTransactionKey!.Value;
        await ReplaceLegAllocationsAsync(purchaseKey, (c1, 30_000m), (c3, 20_000m));

        await Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "Contrato certo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        var return9 = await Tx(change.ReturnPurchaseStorageTransactionKey!.Value);
        var reversals = await AllocationsOf(return9.Key);

        Assert.Equal(2, reversals.Count);
        Assert.Equal(-30_000m, reversals.Single(x => x.PurchaseContractKey == c1.Key).Volume);
        Assert.Equal(-20_000m, reversals.Single(x => x.PurchaseContractKey == c3.Key).Volume);
        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractOf(c3.Key)).AllocatedVolume);
        Assert.Equal(Net, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(0m, return9.AvaiableVolumeToAllocate);
        Assert.Equal(0m, (await Tx(purchaseKey)).AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task HalfAllocatedLeg_ReversesOnlyTheAllocatedPart()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var shipping = await ShipIntoInvoicedLoadAsync(c1, r1);
        var purchaseKey = shipping.PurchaseStorageTransactionKey!.Value;
        await ReplaceLegAllocationsAsync(purchaseKey, (c1, 30_000m));

        await Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "Contrato certo", "tester");

        var change = await ChangeOf(shipping.SalesStorageTransactionKey);
        var return9 = await Tx(change.ReturnPurchaseStorageTransactionKey!.Value);

        var reversal = Assert.Single(await AllocationsOf(return9.Key));
        Assert.Equal(c1.Key, reversal.PurchaseContractKey);
        Assert.Equal(-30_000m, reversal.Volume);
        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(0m, return9.AvaiableVolumeToAllocate);
        Assert.Equal(0m, (await Tx(purchaseKey)).AvaiableVolumeToAllocate);
    }

    [Fact]
    public void Recalculate_ShippingReleaseChangeMarksAreNeverAllocatable()
    {
        StorageTransaction Leg(StorageTransactionType type) => new()
        {
            Key = Guid.NewGuid(),
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = Net,
        };

        var replaced = Leg(StorageTransactionType.Purchase);
        replaced.ReplacedByShippingReleaseChangeKey = Guid.NewGuid();
        replaced.RecalculateAvailableVolume(0m);
        Assert.Equal(0m, replaced.AvaiableVolumeToAllocate);

        var changeReturn = Leg(StorageTransactionType.PurchaseReturn);
        changeReturn.ShippingReleaseChangeKey = Guid.NewGuid();
        changeReturn.RecalculateAvailableVolume(0m);
        Assert.Equal(0m, changeReturn.AvaiableVolumeToAllocate);

        // A 8 nova gerada pela troca segue alocável.
        var newLeg = Leg(StorageTransactionType.Purchase);
        newLeg.ShippingReleaseChangeKey = Guid.NewGuid();
        newLeg.RecalculateAvailableVolume(20_000m);
        Assert.Equal(30_000m, newLeg.AvaiableVolumeToAllocate);
    }
}
