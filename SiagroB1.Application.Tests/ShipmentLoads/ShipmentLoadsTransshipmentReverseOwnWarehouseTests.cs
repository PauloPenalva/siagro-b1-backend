using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1181 fase 2, Task 6 — estornar o transbordo em armazém PRÓPRIO depois que a saída do LOTE
/// já foi vinculada (Task 4): a entrada (Task 3) creditou o armazém pelo <c>TransshipmentReceipt</c>
/// (o 15) e a saída emitiu a liberação a partir do <c>Shipment (1)</c>, não do <c>Receipt (0)</c>.
/// </summary>
/// <remarks>
/// Complementa <see cref="ShipmentLoadsTransshipmentReverseServiceTests"/>: aquela classe cobre o
/// estado que a Task 3 sozinha deixa (só o <c>Receipt</c> vinculado, sem 15 nem saída de lote — ver
/// <see cref="ShipmentLoadsTransshipmentReverseServiceTests.Reverse_OwnWarehouse_OnlyUnlinksTheReceipt"/>,
/// que continua verde sem nenhuma mudança). Esta cobre o estado completo, com a Task 4 já aplicada.
/// </remarks>
public class ShipmentLoadsTransshipmentReverseOwnWarehouseTests
{
    private const string OriginWarehouse = "ARM01";
    private const string TransshipmentWarehouse = "ARM99";
    private const string CardCode = "C0001";
    private const string LotCode = "L-TRANSSHIP-01";

    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsTransshipmentReverseService Service() =>
        new(_db, new ShipmentLoadsMovementLogService(_db.Context));

    private PurchaseContract NewContract(string code = "PC-001")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "F0001",
            ItemCode = "SOJA",
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

    /// <summary>
    /// Carga em transbordo para um armazém PRÓPRIO no estado que a Task 3 + Task 4 deixam: lote de
    /// natureza Transbordo, <c>Receipt (0)</c> confirmado que pesou a entrada e está vinculado ao
    /// transbordo, o crédito do armazém (o 15, também confirmado, achado só pela
    /// <c>ShipmentLoadTransshipmentKey</c>), o <c>Shipment (1)</c> confirmado que pesou a saída do
    /// lote e está vinculado, e a liberação que essa saída emitiu — apontando o <c>Shipment</c>,
    /// não a entrada.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment,
            StorageTransaction Receipt, StorageTransaction WarehouseCredit,
            StorageTransaction LotExit, ShipmentRelease Release)>
        SeedFullOwnWarehouseStateAsync(
            decimal outgoing = 50_000m, decimal entryQuantity = 50_000m,
            decimal lotExitQuantity = 49_000m, decimal shippedQuantity = 0m)
    {
        var lot = new StorageAddress
        {
            Code = LotCode,
            Description = "Lote de transbordo",
            CardCode = CardCode,
            ItemCode = "SOJA",
            WarehouseCode = TransshipmentWarehouse,
            UoM = "KG",
            Nature = StorageAddressNature.Transshipment,
        };
        _db.Context.StorageAddresses.Add(lot);

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
            TransshippedQuantity = outgoing,
        };
        _db.Context.ShipmentLoads.Add(load);

        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = "01",
            StorageAddressCode = LotCode,
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(receipt);

        var lotExit = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "S0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = "01",
            StorageAddressCode = LotCode,
            GrossWeight = lotExitQuantity,
            NetWeight = lotExitQuantity,
            TransactionType = StorageTransactionType.Shipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(lotExit);

        var warehouseCredit = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0015",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = "01",
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(warehouseCredit);

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM PROPRIO",
            OutgoingQuantity = outgoing,
            EntryQuantity = entryQuantity,
        };
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);

        await _db.Context.SaveChangesAsync();

        // Só agora o Key gerado pelo InMemory está disponível para fechar todas as pontas — mesmo
        // precedente dos seeds da Task 5.
        receipt.ShipmentLoadTransshipmentKey = transshipment.Key;
        lotExit.ShipmentLoadTransshipmentKey = transshipment.Key;
        warehouseCredit.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = receipt.Key;
        transshipment.LotExitStorageTransactionKey = lotExit.Key;

        var contract = NewContract();
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = TransshipmentWarehouse,
            ReleasedQuantity = lotExitQuantity,
            ShippedQuantity = shippedQuantity,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.Transshipment,
            GeneratedByStorageTransactionKey = lotExit.Key,
        };
        _db.Context.ShipmentReleases.Add(release);

        await _db.Context.SaveChangesAsync();

        // Sem isto, o Receipt/LotExit/WarehouseCredit ficam RASTREADOS neste DbContext desde o
        // seed, e o relationship-fixup do EF Core zera ShipmentLoadTransshipmentKey sozinho quando
        // o serviço remove o transbordo — mascarando um teste que provaria nada sobre o
        // desvínculo EXPLÍCITO que o serviço faz. Limpar o tracker força o serviço a carregar cada
        // romaneio de novo, como um DbContext por requisição faria em produção. Mesmo precedente de
        // outros testes do módulo (ex.: FixedContractAutoFixationTests).
        _db.Context.ChangeTracker.Clear();

        return (load, transshipment, receipt, warehouseCredit, lotExit, release);
    }

    [Fact]
    public async Task Reverse_OwnWarehouse_CancelsTheWarehouseCreditAndTheRelease()
    {
        var (_, transshipment, _, warehouseCredit, _, release) =
            await SeedFullOwnWarehouseStateAsync();

        await Service().ExecuteAsync(transshipment.Key!.Value, "armazem errado", "tester");

        // Limpa o tracker e relê do banco: a asserção precisa provar o que ficou GRAVADO, não o
        // que o serviço deixou em memória neste mesmo DbContext.
        _db.Context.ChangeTracker.Clear();

        var savedCredit = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == warehouseCredit.Key);
        Assert.Equal(StorageTransactionsStatus.Cancelled, savedCredit.TransactionStatus);

        var savedRelease = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(ReleaseStatus.Cancelled, savedRelease.Status);
        Assert.Contains("armazem errado", savedRelease.CancellationReason);
    }

    /// <summary>
    /// A regra que o usuário definiu (ver o brief da Task 6): os dois romaneios de pesagem são
    /// movimento físico que aconteceu de verdade na balança — o estorno tira só o VÍNCULO com o
    /// transbordo, nunca o status. Cenário completo, com os dois CONFIRMADOS e vinculados: provar
    /// que continuam confirmados depois é o que um teste que já nasceria assim não provaria.
    /// </summary>
    [Fact]
    public async Task Reverse_OwnWarehouse_UnlinksBothWeighingRomaneiosWithoutCancellingThem()
    {
        var (_, transshipment, receipt, _, lotExit, _) = await SeedFullOwnWarehouseStateAsync();

        await Service().ExecuteAsync(transshipment.Key!.Value, null, "tester");

        // Limpa o tracker e relê do banco: prova o ESTADO PERSISTIDO do desvínculo, não o que o
        // change tracker deste DbContext ainda guarda em memória depois do serviço rodar.
        _db.Context.ChangeTracker.Clear();

        var savedReceipt = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == receipt.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, savedReceipt.TransactionStatus);
        Assert.Null(savedReceipt.ShipmentLoadTransshipmentKey);

        var savedLotExit = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == lotExit.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, savedLotExit.TransactionStatus);
        Assert.Null(savedLotExit.ShipmentLoadTransshipmentKey);
    }

    /// <summary>
    /// O lote não volta a ser comum — só sai vinculado a outra carga, num transbordo novo. O valor
    /// tem de ser <c>Transshipment</c> porque NINGUÉM o mudou (nem na criação do lote, nem no
    /// estorno) — um "volta a Regular no estorno" acrescentado por engano tem de derrubar este
    /// teste. O saldo residual (1.000 = 50.000 pesados menos 49.000 recarregados) é lido pela
    /// mesma fórmula de <see cref="StorageAddress.Balance"/> que o produto usa, não reimplementado
    /// em LINQ no teste.
    /// </summary>
    [Fact]
    public async Task Reverse_OwnWarehouse_KeepsTheLeftoverAndTheLotNature()
    {
        var (_, transshipment, _, _, _, _) = await SeedFullOwnWarehouseStateAsync(
            outgoing: 50_000m, entryQuantity: 50_000m, lotExitQuantity: 49_000m);

        await Service().ExecuteAsync(transshipment.Key!.Value, null, "tester");

        // Limpa o tracker e relê do banco pelo mesmo motivo dos outros testes desta classe.
        _db.Context.ChangeTracker.Clear();

        var lot = await _db.Context.StorageAddresses
            .AsNoTracking()
            .Include(x => x.Transactions)
            .SingleAsync(x => x.Code == LotCode);

        Assert.Equal(StorageAddressNature.Transshipment, lot.Nature);
        Assert.Equal(1_000m, lot.Balance);
    }

    /// <summary>
    /// Consumo de VERDADE: <c>ShippedQuantity</c> maior que zero na liberação gerada pela saída do
    /// LOTE — não um campo que já nasceria zero de qualquer jeito.
    /// </summary>
    [Fact]
    public async Task Reverse_OwnWarehouse_RefusesWhenTheReleaseWasAlreadyShipped()
    {
        var (_, transshipment, _, _, _, _) = await SeedFullOwnWarehouseStateAsync(
            shippedQuantity: 5_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, null, "tester"));

        Assert.Contains("Estorne a Expedição de saída antes", error.Message);
    }
}
