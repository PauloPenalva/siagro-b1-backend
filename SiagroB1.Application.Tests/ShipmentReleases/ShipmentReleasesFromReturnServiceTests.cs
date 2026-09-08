using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// Rastreio do contrato de compra a partir do romaneio devolvido, e emissão da liberação que
/// devolve a mercadoria à Expedição de Grãos.
/// </summary>
public class ShipmentReleasesFromReturnServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesFromReturnService Service() => new(_db.Context);

    private PurchaseContract NewContract(string code = "PC-001", string itemCode = "SOJA")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "F0001",
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "01",
            BranchCode = "F01",
            Status = ContractStatus.Approved,
            TotalVolume = 100_000m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        return contract;
    }

    private StorageTransaction NewEntry(decimal quantity = 1000m, string itemCode = "SOJA")
    {
        var entry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "DEV-001",
            CardCode = "C0001",
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            BranchCode = "F01",
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            GrossWeight = quantity,
            NetWeight = quantity,
        };
        _db.Context.StorageTransactions.Add(entry);
        return entry;
    }

    private StorageTransaction NewShipment(string code, decimal weight, Guid? releaseKey = null)
    {
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            BranchCode = "F01",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Invoiced,
            GrossWeight = weight,
            NetWeight = weight,
            ShipmentReleaseKey = releaseKey,
        };
        _db.Context.StorageTransactions.Add(shipment);
        return shipment;
    }

    private ShipmentRelease NewOriginRelease(Guid contractKey)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contractKey,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 50_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(release);
        return release;
    }

    // ---------- cadeia curta ----------

    [Fact]
    public async Task Build_ShortChain_ResolvesContractFromTheShipmentReleaseKey()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var entry = NewEntry();
        var shipment = NewShipment("00001", 1000m, originRelease.Key);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry, [new ReturnedShipmentShare(shipment, 1000m)], "01", "Armazém 01", "tester");

        var release = Assert.Single(result.Releases);
        Assert.Equal(contract.Key, release.PurchaseContractKey);
        Assert.Equal(1000m, release.ReleasedQuantity);
        Assert.Equal(ReleaseOrigin.SalesReturn, release.Origin);
        Assert.Equal(ReleaseStatus.Actived, release.Status);
        Assert.Equal(entry.Key, release.GeneratedByStorageTransactionKey);
        Assert.Equal("01", release.DeliveryLocationCode);
        Assert.Null(release.StorageAddressCode);
        Assert.Equal(0m, result.UntraceableQuantity);
        Assert.Null(result.Note);
    }

    // ---------- cadeia longa ----------

    [Fact]
    public async Task Build_LongChain_ResolvesContractThroughShippingTransactionAndAllocation()
    {
        var contract = NewContract();
        var entry = NewEntry();
        var shipment = NewShipment("00002", 1000m, releaseKey: null);

        var purchase = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "00002-C",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = 1000m,
        };
        _db.Context.StorageTransactions.Add(purchase);
        _db.Context.ShippingTransactions.Add(new ShippingTransaction
        {
            Key = Guid.NewGuid(),
            SalesStorageTransactionKey = shipment.Key,
            PurchaseStorageTransactionKey = purchase.Key,
        });
        _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            StorageTransactionKey = purchase.Key,
            Volume = 1000m,
        });
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry, [new ReturnedShipmentShare(shipment, 1000m)], "01", "Armazém 01", "tester");

        var release = Assert.Single(result.Releases);
        Assert.Equal(contract.Key, release.PurchaseContractKey);
    }

    // ---------- sem rastreio: degrada, não quebra ----------

    [Fact]
    public async Task Build_Untraceable_EmitsNoReleaseAndReportsTheReason()
    {
        var entry = NewEntry();
        var shipment = NewShipment("00003", 1000m, releaseKey: null);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry, [new ReturnedShipmentShare(shipment, 1000m)], "01", "Armazém 01", "tester");

        Assert.Empty(result.Releases);
        Assert.Equal(1000m, result.UntraceableQuantity);
        Assert.NotNull(result.Note);
        Assert.Contains("00003", result.Note);
    }

    [Fact]
    public async Task Build_PartiallyTraceable_EmitsReleaseForTheTraceablePartOnly()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var entry = NewEntry(1500m);
        var traceable = NewShipment("00004", 1000m, originRelease.Key);
        var orphan = NewShipment("00005", 500m, releaseKey: null);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry,
            [new ReturnedShipmentShare(traceable, 1000m), new ReturnedShipmentShare(orphan, 500m)],
            "01", "Armazém 01", "tester");

        var release = Assert.Single(result.Releases);
        Assert.Equal(1000m, release.ReleasedQuantity);
        Assert.Equal(500m, result.UntraceableQuantity);
        Assert.Contains("00005", result.Note!);
    }

    // ---------- portões de sanidade ----------

    [Fact]
    public async Task Build_ContractOfAnotherItem_IsTreatedAsUntraceable()
    {
        // A Expedição de Grãos agrupa por PurchaseContract.ItemCode: um contrato de MILHO faria
        // a soja devolvida aparecer na expedição do milho.
        var contract = NewContract(itemCode: "MILHO");
        var originRelease = NewOriginRelease(contract.Key);
        var entry = NewEntry(itemCode: "SOJA");
        var shipment = NewShipment("00006", 1000m, originRelease.Key);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry, [new ReturnedShipmentShare(shipment, 1000m)], "01", "Armazém 01", "tester");

        Assert.Empty(result.Releases);
        Assert.Equal(1000m, result.UntraceableQuantity);
    }

    [Fact]
    public async Task Build_NoBranchAnywhere_IsTreatedAsUntraceable()
    {
        // A etapa 2 da Expedição agrupa por Branch.ShortName com INNER JOIN: sem filial a
        // liberação nasceria invisível e a mercadoria ficaria presa de novo.
        var contract = NewContract();
        contract.BranchCode = null;
        var originRelease = NewOriginRelease(contract.Key);
        var entry = NewEntry();
        entry.BranchCode = null;
        var shipment = NewShipment("00007", 1000m, originRelease.Key);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry, [new ReturnedShipmentShare(shipment, 1000m)], "01", "Armazém 01", "tester");

        Assert.Empty(result.Releases);
    }

    [Fact]
    public async Task Build_FinishedContract_StillEmitsTheRelease()
    {
        // O estado comercial do contrato não muda o fato físico: o grão está no armazém e
        // precisa de porta de saída.
        var contract = NewContract();
        contract.Status = ContractStatus.Finished;
        var originRelease = NewOriginRelease(contract.Key);
        var entry = NewEntry();
        var shipment = NewShipment("00008", 1000m, originRelease.Key);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry, [new ReturnedShipmentShare(shipment, 1000m)], "01", "Armazém 01", "tester");

        Assert.Single(result.Releases);
    }

    // ---------- vários contratos ----------

    [Fact]
    public async Task Build_TwoContracts_EmitsOneReleasePerContract()
    {
        var contractA = NewContract("PC-A");
        var contractB = NewContract("PC-B");
        var releaseA = NewOriginRelease(contractA.Key);
        var releaseB = NewOriginRelease(contractB.Key);
        var entry = NewEntry(1500m);
        var shipmentA = NewShipment("00009", 1000m, releaseA.Key);
        var shipmentB = NewShipment("00010", 500m, releaseB.Key);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry,
            [new ReturnedShipmentShare(shipmentA, 1000m), new ReturnedShipmentShare(shipmentB, 500m)],
            "01", "Armazém 01", "tester");

        Assert.Equal(2, result.Releases.Count);
        Assert.Equal(1500m, result.Releases.Sum(x => x.ReleasedQuantity));
        Assert.Equal(1000m, result.Releases.Single(x => x.PurchaseContractKey == contractA.Key).ReleasedQuantity);
        Assert.Equal(500m, result.Releases.Single(x => x.PurchaseContractKey == contractB.Key).ReleasedQuantity);
    }

    [Fact]
    public async Task Build_TwoShipmentsOfTheSameContract_EmitsASingleRelease()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var entry = NewEntry(1500m);
        var first = NewShipment("00011", 1000m, originRelease.Key);
        var second = NewShipment("00012", 500m, originRelease.Key);
        await _db.Context.SaveChangesAsync();

        var result = await Service().BuildAsync(
            entry,
            [new ReturnedShipmentShare(first, 1000m), new ReturnedShipmentShare(second, 500m)],
            "01", "Armazém 01", "tester");

        var release = Assert.Single(result.Releases);
        Assert.Equal(1500m, release.ReleasedQuantity);
    }

    // ---------- rateio por peso ----------

    [Fact]
    public void Distribute_SingleShipment_TakesEverything()
    {
        var only = NewShipment("00013", 30_000m);
        var shares = ShipmentReleasesFromReturnService.DistributeByWeight([only], 20_000m);

        Assert.Equal(20_000m, Assert.Single(shares).Quantity);
    }

    [Fact]
    public void Distribute_EqualWeights_SplitsEvenly()
    {
        var a = NewShipment("00014", 30_000m);
        var b = NewShipment("00015", 30_000m);

        var shares = ShipmentReleasesFromReturnService.DistributeByWeight([a, b], 20_000m);

        Assert.Equal(20_000m, shares.Sum(x => x.Quantity));
        Assert.All(shares, s => Assert.Equal(10_000m, s.Quantity));
    }

    [Fact]
    public void Distribute_WithRemainder_SumsExactlyToTheTotal()
    {
        // 10t/20t sobre 7t não divide redondo: o resto vai por maior fração, e a soma tem de
        // bater com o crédito do armazém até o milésimo.
        var a = NewShipment("00016", 10_000m);
        var b = NewShipment("00017", 20_000m);

        var shares = ShipmentReleasesFromReturnService.DistributeByWeight([a, b], 7_000m);

        Assert.Equal(7_000m, shares.Sum(x => x.Quantity));
        Assert.All(shares, s => Assert.True(s.Quantity > 0));
    }

    [Fact]
    public void Distribute_ThreeWayThirds_SumsExactlyToTheTotal()
    {
        var a = NewShipment("00018", 1m);
        var b = NewShipment("00019", 1m);
        var c = NewShipment("00020", 1m);

        var shares = ShipmentReleasesFromReturnService.DistributeByWeight([a, b, c], 10m);

        Assert.Equal(10m, shares.Sum(x => x.Quantity));
    }

    [Fact]
    public void Distribute_NoWeightAtAll_SplitsEvenlyWithoutLosingGrams()
    {
        var a = NewShipment("00021", 0m);
        var b = NewShipment("00022", 0m);

        var shares = ShipmentReleasesFromReturnService.DistributeByWeight([a, b], 5m);

        Assert.Equal(5m, shares.Sum(x => x.Quantity));
    }

    [Fact]
    public async Task Build_ProRatedRefusal_KeepsTheSumEqualToTheWarehouseCredit()
    {
        var contractA = NewContract("PC-A");
        var contractB = NewContract("PC-B");
        var releaseA = NewOriginRelease(contractA.Key);
        var releaseB = NewOriginRelease(contractB.Key);
        var entry = NewEntry(7_000m);
        var shipmentA = NewShipment("00023", 10_000m, releaseA.Key);
        var shipmentB = NewShipment("00024", 20_000m, releaseB.Key);
        await _db.Context.SaveChangesAsync();

        var shares = ShipmentReleasesFromReturnService
            .DistributeByWeight([shipmentA, shipmentB], 7_000m);

        var result = await Service().BuildAsync(entry, shares, "01", "Armazém 01", "tester");

        Assert.Equal(2, result.Releases.Count);
        Assert.Equal(7_000m, result.Releases.Sum(x => x.ReleasedQuantity));
    }
}
