using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1181 fase 2, Task 4 — vincular a saída do LOTE em armazém PRÓPRIO
/// (<see cref="StorageTransactionType.Shipment"/>, o 1) e emitir a liberação pela quantidade REAL
/// carregada. A entrada (Task 3) só creditou o ARMAZÉM; é este vínculo que devolve a mercadoria à
/// Expedição de Grãos — e ele NÃO conclui o transbordo (isso só acontece quando a Expedição de
/// venda, o <c>SalesShipment</c> 7, for vinculada, numa task posterior).
/// </summary>
public class ShipmentLoadsTransshipmentAttachLotExitServiceTests
{
    private const string OriginWarehouse = "ARM01";
    private const string TransshipmentWarehouse = "ARM99";
    private const string CardCode = "C0001";
    private const string TransshipmentLotCode = "L-TRANSSHIP-01";
    private const string OtherLotCode = "L-TRANSSHIP-02";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [OriginWarehouse] = "ARMAZEM ORIGEM",
            [TransshipmentWarehouse] = "ARMAZEM PROPRIO",
        });

    private ShipmentLoadsTransshipmentAttachLotExitService Service() => new(
        _db,
        Warehouses(),
        new ShipmentReleasesFromReturnService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context));

    /// <summary>Grava um lote no armazém do transbordo com natureza Transbordo.</summary>
    private async Task<StorageAddress> SeedLotAsync(string lotCode)
    {
        var lot = new StorageAddress
        {
            Code = lotCode,
            Description = "Lote de transbordo",
            CardCode = CardCode,
            ItemCode = "SOJA",
            WarehouseCode = TransshipmentWarehouse,
            UoM = "KG",
            Nature = StorageAddressNature.Transshipment,
        };
        _db.Context.StorageAddresses.Add(lot);
        await _db.SaveChangesAsync();
        return lot;
    }

    /// <summary>
    /// Carga em transbordo para um armazém PRÓPRIO, com o romaneio de saída da origem, a entrada
    /// (Task 3) já registrada no lote informado, e a saída do lote AINDA não vinculada — o estado
    /// em que a Task 3 deixa a carga.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment, StorageTransaction Origin, StorageTransaction EntryReceipt)>
        SeedRegisteredTransshipmentAsync(
            decimal outgoing = 50_000m,
            decimal entryQuantity = 50_000m,
            Guid? originReleaseKey = null,
            string lotCode = TransshipmentLotCode)
    {
        _db.Context.WarehouseComplements.Add(new WarehouseComplement
        {
            WarehouseCode = TransshipmentWarehouse,
            IsOwn = true,
        });

        await SeedLotAsync(lotCode);

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = outgoing,
        };

        var origin = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0001",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = outgoing,
            NetWeight = outgoing,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentReleaseKey = originReleaseKey,
        };

        var entryReceipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            StorageAddressCode = lotCode,
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM PROPRIO",
            OutgoingQuantity = outgoing,
            EntryQuantity = entryQuantity,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.StorageTransactions.Add(origin);
        _db.Context.StorageTransactions.Add(entryReceipt);
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.SaveChangesAsync();

        // A entrada (Task 3) vincula o Receipt ao transbordo e grava a chave — reproduzido aqui à
        // mão para isolar o teste da Task 4 do serviço da Task 3.
        entryReceipt.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = entryReceipt.Key;
        await _db.SaveChangesAsync();

        return (load, transshipment, origin, entryReceipt);
    }

    /// <summary>
    /// Mesmo estado de <see cref="SeedRegisteredTransshipmentAsync"/>, mas SEM a entrada
    /// registrada — usado só pelo teste que recusa por causa disso.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment)>
        SeedOpenTransshipmentWithoutEntryAsync(decimal outgoing = 50_000m)
    {
        _db.Context.WarehouseComplements.Add(new WarehouseComplement
        {
            WarehouseCode = TransshipmentWarehouse,
            IsOwn = true,
        });

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000002",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = outgoing,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM PROPRIO",
            OutgoingQuantity = outgoing,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.SaveChangesAsync();

        return (load, transshipment);
    }

    /// <summary>
    /// O <c>Shipment (1)</c> que o armazém pesou ao recarregar de volta para o cliente — a saída
    /// do LOTE que a office vai vincular.
    /// </summary>
    private async Task<StorageTransaction> SeedLotExitAsync(
        ShipmentLoad load, string lotCode, decimal grossWeight, string code = "S0001")
    {
        var lotExit = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "F0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            StorageAddressCode = lotCode,
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.Shipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(lotExit);
        await _db.SaveChangesAsync();
        return lotExit;
    }

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
            DeliveryLocationCode = OriginWarehouse,
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        return contract;
    }

    private ShipmentRelease NewOriginRelease(Guid contractKey)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contractKey,
            DeliveryLocationCode = OriginWarehouse,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(release);
        return release;
    }

    private async Task<decimal> TotalAvailableToReleaseAsync(Guid contractKey)
    {
        var contract = await _db.Context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.ShipmentReleases)
            .SingleAsync(x => x.Key == contractKey);

        return contract.TotalAvailableToRelease;
    }

    // ---------- recusas ----------

    [Fact]
    public async Task AttachLotExit_RefusesShipmentFromAnotherLot()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync();
        await SeedLotAsync(OtherLotCode);
        var lotExit = await SeedLotExitAsync(load, OtherLotCode, 49_000m);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester"));

        Assert.Contains("outro lote", error.Message);
    }

    [Fact]
    public async Task AttachLotExit_RefusesWhenTheEntryWasNotRegistered()
    {
        var (load, transshipment) = await SeedOpenTransshipmentWithoutEntryAsync();
        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester"));

        Assert.Contains("ainda não foi registrada", error.Message);
    }

    [Fact]
    public async Task AttachLotExit_RefusesWhenAnExitIsAlreadyAttached()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync();
        var firstExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m, "S0001");

        await Service().ExecuteAsync(transshipment.Key!.Value, firstExit.Key, "tester");

        var secondExit = await SeedLotExitAsync(load, TransshipmentLotCode, 500m, "S0002");

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, secondExit.Key, "tester"));

        Assert.Contains("já tem uma saída de lote vinculada", error.Message);
    }

    // ---------- liberação ----------

    [Fact]
    public async Task AttachLotExit_EmitsOneReleasePerPurchaseContract()
    {
        var contractA = NewContract("PC-A");
        var contractB = NewContract("PC-B");
        var releaseA = NewOriginRelease(contractA.Key);
        var releaseB = NewOriginRelease(contractB.Key);

        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync(
            outgoing: 30_000m, entryQuantity: 30_000m, originReleaseKey: releaseA.Key);

        // Um segundo romaneio de saída da origem, de outro contrato — a carga levou grão dos dois
        // fornecedores.
        var origin2 = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0002",
            CardCode = "C0002",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 10_000m,
            NetWeight = 10_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentReleaseKey = releaseB.Key,
        };
        _db.Context.StorageTransactions.Add(origin2);
        await _db.SaveChangesAsync();

        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 30_000m);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        var releases = await _db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync();

        Assert.Equal(2, releases.Count);
        Assert.Contains(releases, x => x.PurchaseContractKey == contractA.Key);
        Assert.Contains(releases, x => x.PurchaseContractKey == contractB.Key);
        Assert.All(releases, x => Assert.Equal(lotExit.Key, x.GeneratedByStorageTransactionKey));
    }

    /// <summary>
    /// O teste da armadilha (decisão do GAC-1181, ver o brief da Task 4): o lote é REAL e
    /// não-nulo nos dois romaneios (entrada e saída) — se a liberação viesse com
    /// <c>StorageAddressCode</c> nula só porque não havia lote nenhum para copiar, o
    /// <c>Assert.Null</c> não provaria nada. A quantidade é a REAL carregada (49.000), diferente
    /// da pesada na entrada (50.000) e da que saiu da carga (50.000) — provando que não é nenhuma
    /// delas que a liberação carrega.
    /// </summary>
    [Fact]
    public async Task AttachLotExit_ReleaseCarriesTheRealLoadedQuantityAndNoLot()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync(
            outgoing: 50_000m, entryQuantity: 50_000m, originReleaseKey: originRelease.Key);

        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        var release = await _db.Context.ShipmentReleases
            .AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);

        Assert.Equal(49_000m, release.ReleasedQuantity);
        Assert.Equal(lotExit.Key, release.GeneratedByStorageTransactionKey);
        Assert.Null(release.StorageAddressCode);
    }

    /// <summary>
    /// O outro teste da armadilha: compara o saldo do contrato ANTES e DEPOIS, em vez de só ler
    /// <c>ConsumedQuantity</c> — uma liberação recém-criada teria <c>ConsumedQuantity == 0</c> de
    /// qualquer jeito, então aquele campo sozinho não provaria que ela não consome o contrato.
    /// </summary>
    [Fact]
    public async Task AttachLotExit_ReleaseDoesNotConsumeThePurchaseContract()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync(
            outgoing: 50_000m, entryQuantity: 50_000m, originReleaseKey: originRelease.Key);

        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);

        var before = await TotalAvailableToReleaseAsync(contract.Key);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        var after = await TotalAvailableToReleaseAsync(contract.Key);

        Assert.Equal(before, after);
    }

    // ---------- estado da carga ----------

    /// <summary>
    /// O ponto mais fácil de quebrar sem perceber: vincular a saída do LOTE não conclui o
    /// transbordo nem torna a carga faturável. Isso só acontece quando a Expedição de venda
    /// (<c>SalesShipment</c>, o 7) for vinculada, numa task posterior.
    /// </summary>
    [Fact]
    public async Task AttachLotExit_LeavesTheLoadInTransshipmentUntilTheSalesShipmentIsAttached()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync();
        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        var reloaded = await _db.Context.ShipmentLoads
            .AsNoTracking()
            .SingleAsync(x => x.Key == load.Key);

        Assert.Equal(ShipmentLoadStatus.InTransshipment, reloaded.Status);
    }

    /// <summary>
    /// Entraram 50.000 no lote (Receipt da Task 3); saíram 49.000 (este Shipment); sobram 1.000 —
    /// a sobra fica no LOTE, não na liberação (que carrega os 49.000 reais).
    /// </summary>
    [Fact]
    public async Task AttachLotExit_LeftoverStaysInTheLot()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync(
            outgoing: 50_000m, entryQuantity: 50_000m);

        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        var credits = await _db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.StorageAddressCode == TransshipmentLotCode &&
                        x.TransactionType == StorageTransactionType.Receipt &&
                        (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         x.TransactionStatus == StorageTransactionsStatus.Invoiced))
            .SumAsync(x => (decimal?)x.NetWeight) ?? decimal.Zero;

        var debits = await _db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.StorageAddressCode == TransshipmentLotCode &&
                        x.TransactionType == StorageTransactionType.Shipment &&
                        (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         x.TransactionStatus == StorageTransactionsStatus.Invoiced))
            .SumAsync(x => (decimal?)x.NetWeight) ?? decimal.Zero;

        Assert.Equal(1_000m, credits - debits);
    }
}
