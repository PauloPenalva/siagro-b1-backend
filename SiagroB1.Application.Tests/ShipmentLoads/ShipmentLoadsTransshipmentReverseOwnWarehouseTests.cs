using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1181 fase 2, defeito 4 da revisão final (decisão do usuário, 2026-09-22) — estornar o
/// transbordo em armazém PRÓPRIO depois que a saída do LOTE já foi vinculada (Task 4) agora é
/// RECUSADO: o grão já saiu fisicamente do lote, pesado e recarregado no caminhão. Cancelar o
/// crédito do armazém (o <c>TransshipmentReceipt</c>, o 15) inteiro nesse ponto quebrava a
/// invariante que a fase 2 existe para manter — lote e armazém deixavam de terminar IGUAIS: o
/// armazém zerava enquanto a sobra ficava presa só no lote, invisível para a Expedição de Grãos
/// comum (que filtra fora todo lote de natureza Transbordo).
/// </summary>
/// <remarks>
/// Antes desta correção, esta classe testava o CANCELAMENTO bem-sucedido desse mesmo estado
/// completo (Task 3 + Task 4 aplicadas). Os testes antigos
/// (<c>Reverse_OwnWarehouse_CancelsTheWarehouseCreditAndTheRelease</c>,
/// <c>Reverse_OwnWarehouse_UnlinksBothWeighingRomaneiosWithoutCancellingThem</c>,
/// <c>Reverse_OwnWarehouse_KeepsTheLeftoverAndTheLotNature</c> e
/// <c>Reverse_OwnWarehouse_RefusesWhenTheReleaseWasAlreadyShipped</c>) ficaram VERMELHOS com a nova
/// trava (<see cref="ShipmentLoadTransshipmentRules.EnsureLotExitNotAttachedForReversal"/>) e foram
/// substituídos pelos de recusa abaixo — não é a trava pegando largo demais por engano, é
/// exatamente o cenário (saída do lote já vinculada) que ela existe para barrar.
/// <para>
/// Complementa <see cref="ShipmentLoadsTransshipmentReverseServiceTests"/>: aquela classe cobre o
/// estado que a Task 3 sozinha deixa (só o <c>Receipt</c> vinculado, sem 15 nem saída de lote — ver
/// <see cref="ShipmentLoadsTransshipmentReverseServiceTests.Reverse_OwnWarehouse_OnlyUnlinksTheReceipt"/>),
/// que continua estornável exatamente como antes: a trava nova só olha
/// <c>LotExitStorageTransactionKey</c>, presente apenas depois da Task 4.
/// </para>
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

    /// <summary>
    /// Carga em transbordo para um armazém PRÓPRIO no estado que a Task 3 SOZINHA deixa: lote de
    /// natureza Transbordo, <c>Receipt (0)</c> confirmado que pesou a entrada e está vinculado, e o
    /// crédito do armazém (o 15, também confirmado, achado só pela
    /// <c>ShipmentLoadTransshipmentKey</c>) — sem <c>Shipment (1)</c> nem liberação, porque a Task 4
    /// ainda não rodou. É o cenário que a trava nova PRECISA continuar deixando passar.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment,
            StorageTransaction Receipt, StorageTransaction WarehouseCredit)>
        SeedOwnWarehouseStateWithoutLotExitAsync(
            decimal outgoing = 50_000m, decimal entryQuantity = 50_000m)
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

        receipt.ShipmentLoadTransshipmentKey = transshipment.Key;
        warehouseCredit.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = receipt.Key;
        await _db.Context.SaveChangesAsync();

        // Mesmo precedente do seed completo: força o serviço a carregar cada romaneio de novo.
        _db.Context.ChangeTracker.Clear();

        return (load, transshipment, receipt, warehouseCredit);
    }

    /// <summary>
    /// Impede que a trava nova pegue largo demais: SEM saída de lote vinculada, o estorno continua
    /// funcionando exatamente como antes do defeito 4 — cancela o 15 e desvincula o <c>Receipt</c>.
    /// </summary>
    [Fact]
    public async Task Reverse_OwnWarehouse_WithoutLotExit_StillCancelsTheCreditAndUnlinksTheReceipt()
    {
        var (_, transshipment, receipt, warehouseCredit) =
            await SeedOwnWarehouseStateWithoutLotExitAsync();

        await Service().ExecuteAsync(transshipment.Key!.Value, "armazem errado", "tester");

        _db.Context.ChangeTracker.Clear();

        var savedReceipt = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == receipt.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, savedReceipt.TransactionStatus);
        Assert.Null(savedReceipt.ShipmentLoadTransshipmentKey);

        var savedCredit = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == warehouseCredit.Key);
        Assert.Equal(StorageTransactionsStatus.Cancelled, savedCredit.TransactionStatus);

        Assert.Empty(await _db.Context.ShipmentLoadsTransshipments
            .AsNoTracking().Where(x => x.Key == transshipment.Key).ToListAsync());
    }

    /// <summary>
    /// D4 da revisão final: com a saída do lote já vinculada, o estorno é recusado, com a mensagem
    /// nova de <see cref="ShipmentLoadTransshipmentRules.EnsureLotExitNotAttachedForReversal"/> —
    /// não a mensagem antiga de "Expedição de venda vinculada" (que é outra checagem, sobre o
    /// <c>SalesShipment</c>, o 7, que nem existe neste cenário).
    /// </summary>
    [Fact]
    public async Task Reverse_OwnWarehouse_RefusesWhenLotExitIsAlreadyAttached()
    {
        var (_, transshipment, _, _, _, _) = await SeedFullOwnWarehouseStateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, null, "tester"));

        Assert.Contains($"transbordo {transshipment.Sequence}", error.Message);
        Assert.Contains("já tem a saída do lote vinculada", error.Message);
        Assert.Contains("Corrija pela pesagem", error.Message);
    }

    /// <summary>
    /// A recusa é um bloqueio DURO, não uma tentativa parcial: nada muda no banco quando o
    /// estorno é barrado — nem o 15, nem o vínculo do <c>Receipt</c> ou do <c>Shipment (1)</c>,
    /// nem a liberação, nem a linha do transbordo. Prova que a checagem nova roda ANTES de
    /// qualquer <c>BeginTransactionAsync</c>/escrita, como o resto do serviço já faz.
    /// </summary>
    [Fact]
    public async Task Reverse_OwnWarehouse_RefusalWithLotExitAttached_LeavesEverythingUntouched()
    {
        var (_, transshipment, receipt, warehouseCredit, lotExit, release) =
            await SeedFullOwnWarehouseStateAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, null, "tester"));

        // Limpa o tracker e relê do banco: a asserção precisa provar o que ficou GRAVADO, não o
        // que o serviço deixou em memória neste mesmo DbContext.
        _db.Context.ChangeTracker.Clear();

        var savedTransshipment = await _db.Context.ShipmentLoadsTransshipments
            .AsNoTracking().SingleAsync(x => x.Key == transshipment.Key);
        Assert.NotNull(savedTransshipment.LotExitStorageTransactionKey);

        var savedReceipt = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == receipt.Key);
        Assert.NotNull(savedReceipt.ShipmentLoadTransshipmentKey);

        var savedCredit = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == warehouseCredit.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, savedCredit.TransactionStatus);
        Assert.NotNull(savedCredit.ShipmentLoadTransshipmentKey);

        var savedLotExit = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == lotExit.Key);
        Assert.NotNull(savedLotExit.ShipmentLoadTransshipmentKey);

        var savedRelease = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(ReleaseStatus.Actived, savedRelease.Status);
    }

    /// <summary>
    /// A trava nova barra ANTES da checagem antiga de "liberação já embarcada"
    /// (<c>ShippedQuantity > 0</c>): mesmo com a liberação parcialmente consumida, o motivo da
    /// recusa passa a ser a saída do lote vinculada, não o consumo. Substitui o teste antigo
    /// <c>Reverse_OwnWarehouse_RefusesWhenTheReleaseWasAlreadyShipped</c>, que esperava a mensagem
    /// "Estorne a Expedição de saída antes" — inatingível agora que este cenário nunca passa da
    /// trava nova.
    /// </summary>
    [Fact]
    public async Task Reverse_OwnWarehouse_RefusesWhenLotExitIsAttached_EvenIfTheReleaseWasAlreadyShipped()
    {
        var (_, transshipment, _, _, _, _) = await SeedFullOwnWarehouseStateAsync(
            shippedQuantity: 5_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, null, "tester"));

        Assert.Contains("já tem a saída do lote vinculada", error.Message);
    }
}
