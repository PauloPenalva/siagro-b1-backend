using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1181, cenário do chamado: carga na Beatrice, complemento no Mauricio Fernandes, descarga
/// total para beneficiamento na Capal e novo carregamento na Capal — tudo na MESMA carga.
/// </summary>
/// <remarks>
/// Prova, pelos serviços reais e na ordem em que a tela os chama, que o transbordo da fase 1
/// já cobre a carga com DUAS origens (armazéns e contratos diferentes): a homogeneidade não
/// compara armazém, e a entrada no transbordo emite uma liberação por contrato.
/// </remarks>
public class ShipmentLoadsMultiOriginProcessingScenarioTests
{
    private const string Beatrice = "BEATRICE";
    private const string Mauricio = "MAURICIO";
    private const string Capal = "CAPAL";
    private const string Truck = "ABC1D23";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [Beatrice] = "BEATRICE",
            [Mauricio] = "MAURICIO FERNANDES",
            [Capal] = "CAPAL",
        });

    private StorageTransactionsCreateService StorageCreate() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(names: new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            Warehouses(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirm() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private ShipmentLoadsAttachTransactionsService Attach() =>
        new(_db, new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoadsTransshipmentStartService Start() =>
        new(_db, Warehouses(), new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoadsTransshipmentRegisterEntryService RegisterEntry() => new(
        _db,
        Warehouses(),
        new WarehouseComplementService(_db),
        StorageCreate(),
        StorageConfirm(),
        new ShipmentReleasesFromReturnService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentRelease OriginRelease(string contractCode, string warehouse)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = contractCode,
            CardCode = "F-" + warehouse,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouse,
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = warehouse,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        return release;
    }

    private StorageTransaction Shipment(string code, string warehouse, decimal weight, Guid? releaseKey = null)
    {
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = warehouse,
            BranchCode = "01",
            TruckCode = Truck,
            GrossWeight = weight,
            NetWeight = weight,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentReleaseKey = releaseKey,
        };
        _db.Context.StorageTransactions.Add(shipment);
        return shipment;
    }

    [Fact]
    public async Task Load_FromTwoOrigins_ProcessedAndReloadedAtCapal_StaysOneLoad()
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = Truck,
            WarehouseCode = Beatrice,
            Status = ShipmentLoadStatus.Open,
        };
        _db.Context.ShipmentLoads.Add(load);

        var beatriceRelease = OriginRelease("PC-BEATRICE", Beatrice);
        var mauricioRelease = OriginRelease("PC-MAURICIO", Mauricio);
        var beatrice = Shipment("R0001", Beatrice, 30_000m, beatriceRelease.Key);
        var mauricio = Shipment("R0002", Mauricio, 10_000m, mauricioRelease.Key);
        await _db.SaveChangesAsync();

        // 1. Carga na Beatrice e complemento no Mauricio Fernandes, em momentos diferentes.
        await Attach().ExecuteAsync(load.Key, [beatrice.Key], null, "tester");
        await Attach().ExecuteAsync(load.Key, [mauricio.Key], null, "tester");

        // 2. Descarga total na Capal para beneficiamento: 40 t saem, 39,2 t entram (quebra 0,8 t).
        var transshipment = await Start().ExecuteAsync(load.Key, Capal, DateTime.Today, "Beneficiamento", "tester");
        Assert.Equal(40_000m, transshipment.OutgoingQuantity);

        await RegisterEntry().ExecuteAsync(transshipment.Key!.Value, 39_200m, DateTime.Today, null, "tester");

        // Uma liberação por contrato de origem, na Capal, rateada por peso (3/4 e 1/4).
        var releases = await _db.Context.ShipmentReleases.AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync();
        Assert.Equal(2, releases.Count);
        Assert.All(releases, x => Assert.Equal(Capal, x.DeliveryLocationCode));
        Assert.Equal(29_400m, releases.Single(x => x.PurchaseContractKey == beatriceRelease.PurchaseContractKey).ReleasedQuantity);
        Assert.Equal(9_800m, releases.Single(x => x.PurchaseContractKey == mauricioRelease.PurchaseContractKey).ReleasedQuantity);

        var loadInProcessing = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.InTransshipment, loadInProcessing.Status);

        // 3. Novo carregamento na Capal noutro caminhão (o primeiro quebrou): uma Expedição por
        //    liberação, as duas no veículo novo.
        var exitA = Shipment("R0003", Capal, 29_000m);
        var exitB = Shipment("R0004", Capal, 9_800m);
        exitA.TruckCode = "XYZ9K87";
        exitB.TruckCode = "XYZ9K87";
        await _db.SaveChangesAsync();
        await Attach().ExecuteAsync(load.Key, [exitA.Key, exitB.Key], transshipment.Key, "tester");

        var final = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.Open, final.Status);
        Assert.Equal("XYZ9K87", final.TruckCode);
        Assert.Equal(78_800m, final.TotalQuantity);       // 40 t das origens + 38,8 t da Capal
        Assert.Equal(40_000m, final.TransshippedQuantity);
        Assert.Equal(38_800m, final.AvailableQuantity);   // só o que sai da Capal vai para faturar

        var composition = await _db.Context.StorageTransactions.AsNoTracking()
            .CountAsync(x => x.ShipmentLoadKey == load.Key);
        Assert.Equal(4, composition);
    }
}
