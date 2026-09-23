using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.WeighingTickets;

/// <summary>
/// GAC-1181 fase 2 (redesenho, 2026-09-23) — a liberação de embarque do transbordo deixa de
/// nascer do botão "Vincular Saída do Lote" na carga e passa a nascer na CONFIRMAÇÃO do romaneio de
/// saída do lote, na pesagem: <see cref="WeighingTicketsCompletedService"/> agora chama
/// <see cref="ShipmentLoadsTransshipmentAttachLotExitService"/> internamente
/// (<see cref="Infra.Enums.CommitMode.Deferred"/>), resolvendo o transbordo dono da saída pelo
/// CAMINHÃO — "o mesmo caminhão que entrou com a mercadoria vai sair com ela" (decisão do usuário) —
/// via <see cref="ShipmentLoadTransshipmentRules.ResolveOpenTransshipmentForLotExitAsync"/>.
/// </summary>
/// <remarks>
/// As regras de negócio do vínculo em si (quem pode ser vinculado, o que a liberação carrega) não
/// mudaram — continuam cobertas por <c>ShipmentLoadsTransshipmentAttachLotExitServiceTests</c>. Esta
/// classe cobre só o GATILHO NOVO: como a pesagem acha (ou recusa achar) o transbordo dono da saída.
/// </remarks>
public class WeighingTicketTransshipmentLotExitTriggerTests
{
    private const string TransshipmentWarehouse = "ARM99";
    private const string OriginWarehouse = "ARM01";
    private const string CardCode = "C0001";
    private const string SupplierCardCode = "F0001";
    private const string ItemCode = "SOJA";
    private const string LotCode = "L-TRANSSHIP-01";
    private const string BranchCode = "01";
    private const string TruckCode = "ABC1D23";
    private const string OtherTruckCode = "XYZ9W88";

    private static WeighingTicketsCompletedService Service(IUnitOfWork db) => new(
        db,
        new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
        new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
        new StorageTransactionsCreateService(
            db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
            new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
            new FakeWarehouseService(new() { [TransshipmentWarehouse] = "Armazém Transbordo" }),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentReleaseMovementGuardService(db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance),
        new StorageTransactionsConfirmedService(
            db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentReleaseMovementGuardService(db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance),
        new StorageAddressesGetService(db, NullLogger<StorageAddressesGetService>.Instance),
        new ShipmentLoadsTransshipmentAttachLotExitService(
            db,
            new FakeWarehouseService(new() { [TransshipmentWarehouse] = "Armazém Transbordo" }),
            new ShipmentReleasesFromReturnService(db.Context),
            new ShipmentLoadsMovementLogService(db.Context)),
        new FakeStringLocalizer<Resource>(),
        NullLogger<WeighingTicketsCompletedService>.Instance);

    private static async Task<IUnitOfWork> SeedLotAsync(
        StorageAddressNature nature = StorageAddressNature.Transshipment)
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = LotCode,
            Description = "Lote de transbordo",
            CardCode = CardCode,
            ItemCode = ItemCode,
            WarehouseCode = TransshipmentWarehouse,
            UoM = "KG",
            Nature = nature,
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static PurchaseContract NewContract(IUnitOfWork db, string code)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = SupplierCardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = OriginWarehouse,
            BranchCode = BranchCode,
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        db.Context.PurchaseContracts.Add(contract);
        return contract;
    }

    private static ShipmentRelease NewOriginRelease(IUnitOfWork db, Guid contractKey)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contractKey,
            DeliveryLocationCode = OriginWarehouse,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        db.Context.ShipmentReleases.Add(release);
        return release;
    }

    /// <summary>
    /// Carga em transbordo, com o romaneio de saída de origem e a entrada (Task 3) já registrada
    /// no lote — o estado em que a Task 3 deixa a carga, pronto para a saída do lote pela pesagem.
    /// </summary>
    private static async Task<ShipmentLoadTransshipment> SeedOpenTransshipmentAsync(
        IUnitOfWork db,
        string truckCode,
        decimal entryQuantity,
        string loadCode = "CG000001",
        int sequence = 1,
        Guid? originReleaseKey = null)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = loadCode,
            BranchCode = BranchCode,
            ItemCode = ItemCode,
            ItemName = "Soja em grãos",
            UnitOfMeasureCode = "KG",
            TruckCode = truckCode,
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = entryQuantity,
        };

        var origin = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = SupplierCardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = BranchCode,
            TruckCode = truckCode,
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentReleaseKey = originReleaseKey,
        };

        var entryReceipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = SupplierCardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = BranchCode,
            StorageAddressCode = LotCode,
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = sequence,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "Armazém Transbordo",
            OutgoingQuantity = entryQuantity,
            EntryQuantity = entryQuantity,
        };

        db.Context.ShipmentLoads.Add(load);
        db.Context.StorageTransactions.Add(origin);
        db.Context.StorageTransactions.Add(entryReceipt);
        db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await db.SaveChangesAsync();

        // Task 3 vincula o Receipt ao transbordo e grava a chave — reproduzido aqui à mão, mesmo
        // precedente de ShipmentLoadsTransshipmentAttachLotExitServiceTests.
        entryReceipt.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = entryReceipt.Key;
        await db.SaveChangesAsync();

        return transshipment;
    }

    /// <summary>Ticket de saída (Shipment) pesado no lote de transbordo.</summary>
    private static WeighingTicket NewShipmentTicket(
        string truckCode, int firstWeigh, int secondWeigh) => new()
    {
        Key = Guid.NewGuid(),
        Type = WeighingTicketType.Shipment,
        ItemCode = ItemCode,
        CardCode = CardCode,
        BranchCode = BranchCode,
        TruckCode = truckCode,
        TruckDriverCode = "1",
        Stage = WeighingTicketStage.ReadyForCompleting,
        StorageAddressCode = LotCode,
        FirstWeighValue = firstWeigh,
        SecondWeighValue = secondWeigh,
    };

    // ---------- sucesso ----------

    /// <summary>
    /// O caminho feliz do redesenho: confirmar a saída no lote de transbordo, com um transbordo
    /// aberto do MESMO caminhão, emite a liberação — sem lote, pela quantidade REAL pesada na
    /// saída (50.000 - 1.000 = 49.000), não pela quantidade que entrou (50.000).
    /// </summary>
    [Fact]
    public async Task ConfirmedExit_SameTruckAsOpenTransshipment_EmitsReleaseWithoutLotForTheRealQuantity()
    {
        var db = await SeedLotAsync();
        var contract = NewContract(db, "PC-001");
        var originRelease = NewOriginRelease(db, contract.Key);
        await db.SaveChangesAsync();

        await SeedOpenTransshipmentAsync(
            db, TruckCode, entryQuantity: 50_000m, originReleaseKey: originRelease.Key);

        var ticket = NewShipmentTicket(TruckCode, firstWeigh: 50_000, secondWeigh: 1_000);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Service(db).ExecuteAsync(ticket.Key, "tester");

        var lotExit = await db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.WeighingTicketKey == ticket.Key);

        Assert.Equal(StorageTransactionsStatus.Confirmed, lotExit.TransactionStatus);
        Assert.NotNull(lotExit.ShipmentLoadTransshipmentKey);

        var release = await db.Context.ShipmentReleases
            .AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);

        Assert.Equal(49_000m, release.ReleasedQuantity);
        Assert.Null(release.StorageAddressCode);
        Assert.Equal(lotExit.Key, release.GeneratedByStorageTransactionKey);

        var transshipment = await db.Context.ShipmentLoadsTransshipments
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(lotExit.Key, transshipment.LotExitStorageTransactionKey);
    }

    // ---------- recusas ----------

    /// <summary>
    /// O teste que prova o discriminador: sem ele, nada prende o filtro de caminhão contra um
    /// "corrigir" que o torne incondicional.
    /// </summary>
    [Fact]
    public async Task ConfirmedExit_DifferentTruckFromTheOpenTransshipment_Refuses()
    {
        var db = await SeedLotAsync();
        await SeedOpenTransshipmentAsync(db, TruckCode, entryQuantity: 50_000m);

        var ticket = NewShipmentTicket(OtherTruckCode, firstWeigh: 50_000, secondWeigh: 1_000);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service(db).ExecuteAsync(ticket.Key, "tester"));

        Assert.Contains("não tem entrada de transbordo registrada", error.Message);
        Assert.Contains(OtherTruckCode, error.Message);

        Assert.Empty(await db.Context.ShipmentReleases
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());
    }

    [Fact]
    public async Task ConfirmedExit_LotWithoutAnyOpenTransshipment_Refuses()
    {
        var db = await SeedLotAsync();
        // Nenhum ShipmentLoadTransshipment seedado — lote de transbordo sem entrada registrada.

        // Saldo suficiente no lote para a saída não ser recusada pela trava de saldo
        // (WeighingTicketsCompletedService.Validate), que é ortogonal ao gatilho testado aqui —
        // sem esta linha o teste pegaria a recusa ERRADA (EXCEPTION_00003, saldo) em vez da que
        // prova o gatilho novo.
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            StorageAddressCode = LotCode,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = 50_000m,
            CardCode = CardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
        });
        await db.SaveChangesAsync();

        var ticket = NewShipmentTicket(TruckCode, firstWeigh: 50_000, secondWeigh: 1_000);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service(db).ExecuteAsync(ticket.Key, "tester"));

        Assert.Contains("não tem entrada de transbordo registrada", error.Message);

        Assert.Empty(await db.Context.ShipmentReleases
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());
    }

    [Fact]
    public async Task ConfirmedExit_TwoOpenTransshipmentsSameTruckSameLot_Refuses()
    {
        var db = await SeedLotAsync();
        await SeedOpenTransshipmentAsync(
            db, TruckCode, entryQuantity: 30_000m, loadCode: "CG000001", sequence: 1);
        await SeedOpenTransshipmentAsync(
            db, TruckCode, entryQuantity: 20_000m, loadCode: "CG000002", sequence: 1);

        var ticket = NewShipmentTicket(TruckCode, firstWeigh: 50_000, secondWeigh: 1_000);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service(db).ExecuteAsync(ticket.Key, "tester"));

        Assert.Contains("Mais de um transbordo em aberto", error.Message);
        Assert.Contains("CG000001", error.Message);
        Assert.Contains("CG000002", error.Message);

        Assert.Empty(await db.Context.ShipmentReleases
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());
    }

    // ---------- não-regressão ----------

    /// <summary>
    /// A pesagem normal (lote de natureza Comum) não pode virar regressão: o gatilho novo só entra
    /// em jogo quando o lote é de natureza Transbordo. Sem isto, toda saída pesada num lote comum
    /// pagaria o custo de uma varredura de transbordos que nunca vai achar nada.
    /// </summary>
    [Fact]
    public async Task ConfirmedExit_RegularLot_DoesNothingAndDoesNotThrow()
    {
        var db = await SeedLotAsync(nature: StorageAddressNature.Regular);

        // Saldo suficiente no lote comum para a saída não ser recusada pela trava de saldo
        // (WeighingTicketsCompletedService.Validate), que é ortogonal ao gatilho testado aqui.
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            StorageAddressCode = LotCode,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = 50_000m,
            CardCode = CardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
        });
        await db.SaveChangesAsync();

        var ticket = NewShipmentTicket(TruckCode, firstWeigh: 50_000, secondWeigh: 1_000);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Service(db).ExecuteAsync(ticket.Key, "tester");

        var lotExit = await db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.WeighingTicketKey == ticket.Key);

        Assert.Equal(StorageTransactionsStatus.Confirmed, lotExit.TransactionStatus);
        Assert.Null(lotExit.ShipmentLoadTransshipmentKey);
        Assert.Empty(await db.Context.ShipmentReleases
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());
    }
}
