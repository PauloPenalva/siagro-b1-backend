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
        ShipmentLoad load,
        string lotCode,
        decimal grossWeight,
        string code = "S0001",
        StorageTransactionType type = StorageTransactionType.Shipment,
        decimal? netWeight = null)
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
            NetWeight = netWeight ?? grossWeight,
            TransactionType = type,
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

    /// <summary>
    /// Cobertura da checagem de <c>TransactionType</c> acrescentada a
    /// <see cref="ShipmentLoadTransshipmentRules.EnsureLotExitIsUsable"/> além das três que o
    /// brief pedia: confirmado, no lote certo, sem vínculo anterior — mas de um tipo que não é
    /// <see cref="StorageTransactionType.Shipment"/> (aqui, um <c>Receipt</c>).
    /// </summary>
    [Fact]
    public async Task AttachLotExit_RefusesWhenTheStorageTransactionIsNotAShipment()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync();
        var wrongType = await SeedLotExitAsync(
            load, TransshipmentLotCode, 49_000m, "S0001", StorageTransactionType.Receipt);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, wrongType.Key, "tester"));

        Assert.Contains("não é uma saída de armazenagem", error.Message);
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

    // ---------- vínculo ----------

    /// <summary>
    /// A escrita central deste serviço: <c>lotExit.ShipmentLoadTransshipmentKey</c> GRAVADO
    /// apontando o transbordo — não só <c>transshipment.LotExitStorageTransactionKey</c>, o lado
    /// que <see cref="AttachLotExit_RefusesWhenAnExitIsAlreadyAttached"/> já cobre (de lado, ao
    /// decidir a recusa da segunda tentativa por ele). É a chave NO ROMANEIO, e não a chave no
    /// transbordo, que <c>ShipmentLoadsTransshipmentReverseService</c> (Task 6) usa para achar e
    /// desvincular a saída do lote no estorno — sem teste direto aqui, uma regressão nessa
    /// atribuição só apareceria mais tarde, no estorno.
    /// </summary>
    [Fact]
    public async Task AttachLotExit_PersistsTheLinkOnTheLotExitTransaction()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync();
        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        // Limpa o tracker e relê do banco: a asserção precisa provar o que ficou GRAVADO, não o
        // que o serviço deixou em memória neste mesmo DbContext — mesmo precedente de
        // ShipmentLoadsTransshipmentReverseOwnWarehouseTests (GAC-1181, Task 6, Round 1).
        _db.Context.ChangeTracker.Clear();

        var savedLotExit = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.Key == lotExit.Key);

        Assert.Equal(transshipment.Key, savedLotExit.ShipmentLoadTransshipmentKey);
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
    /// <remarks>
    /// O saldo é lido por <see cref="StorageAddress.Balance"/> — a mesma fórmula que o sistema usa
    /// (<c>TotalReceipt - (TotalShipment + TotalQualityLoss)</c>), carregando o lote com seus
    /// <see cref="StorageAddress.Transactions"/>. Reimplementar a soma em LINQ no teste provaria só
    /// a aritmética do teste: se a fórmula de saldo do produto regredisse, uma cópia dela aqui
    /// continuaria verde.
    /// </remarks>
    [Fact]
    public async Task AttachLotExit_LeftoverStaysInTheLot()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync(
            outgoing: 50_000m, entryQuantity: 50_000m);

        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        var lot = await _db.Context.StorageAddresses
            .AsNoTracking()
            .Include(x => x.Transactions)
            .SingleAsync(x => x.Code == TransshipmentLotCode);

        Assert.Equal(1_000m, lot.Balance);
    }

    // ---------- revisão final da fase 2 ----------

    /// <summary>
    /// DEFEITO 1 (alto) da revisão final, guarda 1/3: o discriminador precisa ser ESTRUTURAL — o
    /// TIPO do romaneio de entrada persistido, não <c>IsOwn</c> ao vivo (regra temporal do
    /// <c>&lt;remarks&gt;</c> de <see cref="ShipmentLoadTransshipmentRules"/>). Em armazém de
    /// TERCEIRO a entrada é o próprio <c>TransshipmentReceipt</c> (o 15), sem lote — a revisão
    /// provou por execução que, sem esta recusa, um <c>Shipment (1)</c> confirmado e sem lote
    /// (a tela de Romaneios permite criar um assim: "Lote de Armazenagem" não é obrigatório para
    /// Saída) passava pela checagem de "mesmo lote" (dois <c>null</c> via <c>string.Equals</c>) e
    /// emitia uma SEGUNDA liberação de origem Transbordo sobre a mesma mercadoria (50.000 e
    /// 49.000 liberáveis para 50.000 kg reais).
    /// </summary>
    [Fact]
    public async Task AttachLotExit_RefusesForThirdPartyWarehouseTransshipment()
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000009",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = 50_000m,
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
            GrossWeight = 50_000m,
            NetWeight = 50_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM TERCEIRO",
            OutgoingQuantity = 50_000m,
            EntryQuantity = 50_000m,
        };

        // O 15 de terceiro: por desenho, NÃO tem lote.
        var thirdPartyEntry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            StorageAddressCode = null,
            GrossWeight = 50_000m,
            NetWeight = 50_000m,
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.StorageTransactions.Add(origin);
        _db.Context.StorageTransactions.Add(thirdPartyEntry);
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.SaveChangesAsync();

        thirdPartyEntry.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = thirdPartyEntry.Key;
        await _db.SaveChangesAsync();

        // Um Shipment (1) confirmado, SEM lote e sem vínculo — o romaneio "solto" que a revisão
        // usou para provar a liberação em dobro.
        var rogueExit = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "S0099",
            CardCode = "F0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            StorageAddressCode = null,
            GrossWeight = 49_000m,
            NetWeight = 49_000m,
            TransactionType = StorageTransactionType.Shipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(rogueExit);
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, rogueExit.Key, "tester"));

        Assert.Contains("armazém de terceiro", error.Message);

        Assert.Empty(await _db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());
    }

    /// <summary>
    /// DEFEITO 1 (alto) da revisão final, guarda 2/3: sem exigir o transbordo ainda ABERTO, nada
    /// impedia vincular uma saída de lote a um transbordo cuja Expedição de venda (o
    /// <c>SalesShipment</c>, 7) já tivesse sido vinculada — reabrindo um transbordo já concluído.
    /// </summary>
    [Fact]
    public async Task AttachLotExit_RefusesWhenTheTransshipmentIsAlreadyClosed()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync();

        // A Expedição de venda que fecha o transbordo (Task 7) — HasOpenTransshipmentAsync/
        // IsClosedAsync leem exatamente este vínculo.
        var closingSalesShipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "SS0001",
            CardCode = "F0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            GrossWeight = 49_000m,
            NetWeight = 49_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadTransshipmentKey = transshipment.Key,
        };
        _db.Context.StorageTransactions.Add(closingSalesShipment);
        await _db.SaveChangesAsync();

        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 500m, "S0099");

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester"));

        Assert.Contains("já foi concluído", error.Message);
    }

    /// <summary>
    /// DEFEITO 1 (alto) da revisão final, guarda 3/3: <c>string.Equals(null, null)</c> devolve
    /// <c>true</c> — a checagem de "mesmo lote" precisa recusar quando QUALQUER um dos lados não
    /// tem lote, e não só quando os códigos são diferentes. Força os dois lados sem
    /// <c>StorageAddressCode</c> (cenário defensivo: numa carga real
    /// <c>EnsureReceiptIsFromTransshipmentLotAsync</c> já barraria uma entrada própria sem lote
    /// antes de chegar aqui, mas esta checagem precisa se sustentar sozinha).
    /// </summary>
    [Fact]
    public async Task AttachLotExit_RefusesWhenNeitherSideHasALot()
    {
        var (load, transshipment, _, entryReceipt) = await SeedRegisteredTransshipmentAsync();

        entryReceipt.StorageAddressCode = null;
        await _db.SaveChangesAsync();

        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);
        lotExit.StorageAddressCode = null;
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester"));

        Assert.Contains("outro lote", error.Message);
    }

    /// <summary>
    /// DEFEITO 3 (médio) da revisão final: o LOTE é debitado por <c>NetWeight</c> na confirmação
    /// do <c>Shipment (1)</c> — a liberação que devolve a mercadoria à Expedição de Grãos precisa
    /// usar a MESMA grandeza, senão carrega peso que nunca saiu do lote. Nenhum outro teste desta
    /// suíte tem <c>NetWeight != GrossWeight</c>, então nenhum outro cobre este caminho.
    /// </summary>
    [Fact]
    public async Task AttachLotExit_ReleaseUsesNetWeightWhenTheExitHasADiscount()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync(
            outgoing: 50_000m, entryQuantity: 50_000m, originReleaseKey: originRelease.Key);

        var lotExit = await SeedLotExitAsync(
            load, TransshipmentLotCode, grossWeight: 49_000m, netWeight: 48_500m);

        await Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester");

        var release = await _db.Context.ShipmentReleases
            .AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);

        Assert.Equal(48_500m, release.ReleasedQuantity);
    }

    /// <summary>
    /// DEFEITO 5 (médio-baixo) da revisão final: <c>EnsureLotExitIsUsable</c> não conferia
    /// filial/produto/unidade contra a carga, ao contrário do irmão
    /// <c>EnsureOwnWarehouseReceiptIsUsable</c>. A liberação herda a filial do romaneio de SAÍDA,
    /// não da carga (<c>ShipmentReleasesFromReturnService.Create</c>): sem esta checagem, um
    /// ticket de saída aberto na filial errada faz a liberação nascer nessa filial e sumir da
    /// Expedição da carga, que agrupa por filial com INNER JOIN.
    /// </summary>
    [Fact]
    public async Task AttachLotExit_RefusesWhenTheBranchDoesNotMatchTheLoad()
    {
        var (load, transshipment, _, _) = await SeedRegisteredTransshipmentAsync();
        var lotExit = await SeedLotExitAsync(load, TransshipmentLotCode, 49_000m);
        lotExit.BranchCode = "99";
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, lotExit.Key, "tester"));

        Assert.Contains("não corresponde ao produto, filial ou unidade", error.Message);
    }
}
