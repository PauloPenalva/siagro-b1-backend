using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Papel do romaneio no transbordo da carga (GAC-1181, Task 7): a saída que recarrega no armazém
/// intermediário é vinculada com o carimbo do transbordo, o que trava composição, cancelamento e
/// exclusão enquanto ele existir, e a leitura do grid alcança a entrada.
/// </summary>
/// <remarks>
/// Cobre <c>Attach</c>, <c>Detach</c>, <c>Cancel</c>, <c>Delete</c> e
/// <see cref="ShipmentLoadsGetService.QueryTransactions"/> — os cinco caminhos que este passo
/// altera. Nenhum destes cenários tem transbordo nenhum na carga Normal comum nem na de Remoção:
/// os testes de regressão de cada serviço (nos arquivos próprios) continuam sendo a prova de que
/// nada mudou para elas.
/// </remarks>
public class ShipmentLoadsAttachTransshipmentExitTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsAttachTransactionsService AttachService() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoadsDetachTransactionsService DetachService() => new(
        _db,
        new ShipmentLoadsCompositionGuardService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoadsCancelService CancelService() => new(
        _db,
        new ShipmentLoadsCompositionGuardService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoadsDeleteService DeleteService() => new(
        _db, new ShipmentLoadsCompositionGuardService(_db.Context));

    private ShipmentLoadsGetService GetService() =>
        new(_db, NullLogger<ShipmentLoadsGetService>.Instance);

    private ShipmentLoad Load(
        ShipmentLoadStatus status = ShipmentLoadStatus.Open,
        decimal totalQuantity = 30_000m)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
            TotalQuantity = totalQuantity,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction Shipment(
        string code,
        string warehouseCode = "ARM01",
        decimal grossWeight = 30_000m,
        StorageTransactionType type = StorageTransactionType.SalesShipment,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        Guid? shipmentLoadKey = null,
        Guid? shipmentLoadTransshipmentKey = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = warehouseCode,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = type,
            TransactionStatus = status,
            ShipmentLoadKey = shipmentLoadKey,
            ShipmentLoadTransshipmentKey = shipmentLoadTransshipmentKey,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    private ShipmentLoadTransshipment Transshipment(
        ShipmentLoad load,
        int sequence = 1,
        string warehouseCode = "ARM99",
        Guid? entryStorageTransactionKey = null,
        decimal outgoingQuantity = 30_000m)
    {
        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = sequence,
            WarehouseCode = warehouseCode,
            WarehouseName = "ARMAZEM TERCEIRO",
            OutgoingQuantity = outgoingQuantity,
            EntryStorageTransactionKey = entryStorageTransactionKey,
        };

        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        return transshipment;
    }

    // ─────────────────────────────── Attach ───────────────────────────────

    /// <summary>
    /// A saída que recarrega no armazém do transbordo entra com as DUAS chaves — é o que faz
    /// <c>HasOpenTransshipmentAsync</c> parar de enxergar o transbordo como aberto.
    /// </summary>
    [Fact]
    public async Task Attach_WithTransshipmentKey_StampsTheRole()
    {
        var load = Load(ShipmentLoadStatus.InTransshipment);
        var transshipment = Transshipment(
            load, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        var exit = Shipment("R2", warehouseCode: "ARM99");
        await _db.Context.SaveChangesAsync();

        await AttachService().ExecuteAsync(load.Key, [exit.Key], transshipment.Key, "tester");

        var saved = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == exit.Key);
        Assert.Equal(load.Key, saved.ShipmentLoadKey);
        Assert.Equal(transshipment.Key, saved.ShipmentLoadTransshipmentKey);
    }

    /// <summary>
    /// Regra do briefing: "O romaneio {Code} é do armazém {X} e o transbordo é no armazém {Y}."
    /// Sem esta trava, uma saída de outro armazém entraria carimbada com um transbordo ao qual
    /// não pertence.
    /// </summary>
    [Fact]
    public async Task Attach_WithTransshipmentKey_RefusesOtherWarehouse()
    {
        var load = Load(ShipmentLoadStatus.InTransshipment);
        var transshipment = Transshipment(
            load, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        var exit = Shipment("R2", warehouseCode: "ARM05");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => AttachService().ExecuteAsync(load.Key, [exit.Key], transshipment.Key, "tester"));

        Assert.Contains("R2", error.Message);
        Assert.Contains("ARM05", error.Message);
        Assert.Contains("ARM99", error.Message);
        Assert.Null((await _db.Context.StorageTransactions.SingleAsync(x => x.Key == exit.Key)).ShipmentLoadKey);
    }

    /// <summary>
    /// Sem entrada registrada não existe mercadoria pronta no armazém intermediário para
    /// recarregar — vincular a saída antes disso carimbaria um transbordo que nunca recebeu nada.
    /// </summary>
    [Fact]
    public async Task Attach_WithTransshipmentKey_RefusesBeforeTheEntryIsRegistered()
    {
        var load = Load(ShipmentLoadStatus.InTransshipment);
        var transshipment = Transshipment(load, warehouseCode: "ARM99", entryStorageTransactionKey: null);
        var exit = Shipment("R2", warehouseCode: "ARM99");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => AttachService().ExecuteAsync(load.Key, [exit.Key], transshipment.Key, "tester"));

        Assert.Contains("entrada", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await _db.Context.StorageTransactions.SingleAsync(x => x.Key == exit.Key)).ShipmentLoadKey);
    }

    /// <summary>
    /// Regressão: um transbordo em OUTRO armazém não pode interferir na vinculação comum de uma
    /// saída de origem, quando ninguém informou <c>transshipmentKey</c>.
    /// </summary>
    [Fact]
    public async Task Attach_WithoutTransshipmentKey_KeepsTodaysBehaviour()
    {
        var load = Load(ShipmentLoadStatus.Planned, totalQuantity: 0m);
        Transshipment(load, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        var a = Shipment("R1", warehouseCode: "ARM01");
        await _db.Context.SaveChangesAsync();

        await AttachService().ExecuteAsync(load.Key, [a.Key], null, "tester");

        var saved = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == a.Key);
        Assert.Equal(load.Key, saved.ShipmentLoadKey);
        Assert.Null(saved.ShipmentLoadTransshipmentKey);
        Assert.Equal(30_000m, (await _db.Context.ShipmentLoads.SingleAsync()).TotalQuantity);
    }

    /// <summary>
    /// Sem a chave, um romaneio do MESMO armazém de um transbordo já recebido é a saída dele
    /// disfarçada de vinculação comum — a mensagem orienta o caminho certo em vez de deixar
    /// entrar sem o carimbo.
    /// </summary>
    [Fact]
    public async Task Attach_WithoutTransshipmentKey_RefusesShipmentFromARegisteredTransshipmentWarehouse()
    {
        var load = Load(ShipmentLoadStatus.InTransshipment);
        Transshipment(load, sequence: 1, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        var exit = Shipment("R2", warehouseCode: "ARM99");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => AttachService().ExecuteAsync(load.Key, [exit.Key], null, "tester"));

        Assert.Contains("R2", error.Message);
        Assert.Contains("transbordo 1", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await _db.Context.StorageTransactions.SingleAsync(x => x.Key == exit.Key)).ShipmentLoadKey);
    }

    /// <summary>
    /// Revisão da Task 7: dois transbordos da MESMA carga podem compartilhar o armazém (um
    /// encerrado, outro reaberto depois). O antigo <c>FirstOrDefault</c> citava só o primeiro da
    /// lista — podendo nomear o transbordo ERRADO. Ambíguo, a mensagem agora lista os dois em vez
    /// de arriscar um palpite.
    /// </summary>
    [Fact]
    public async Task Attach_WithoutTransshipmentKey_NamesAllCandidatesWhenTheWarehouseIsAmbiguous()
    {
        var load = Load(ShipmentLoadStatus.InTransshipment);
        Transshipment(load, sequence: 1, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        Transshipment(load, sequence: 2, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        var exit = Shipment("R2", warehouseCode: "ARM99");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => AttachService().ExecuteAsync(load.Key, [exit.Key], null, "tester"));

        Assert.Contains("R2", error.Message);
        Assert.Contains("1, 2", error.Message);
        Assert.Null((await _db.Context.StorageTransactions.SingleAsync(x => x.Key == exit.Key)).ShipmentLoadKey);
    }

    // ─────────────────────────────── Detach ───────────────────────────────

    /// <summary>
    /// Desvincular a própria saída do transbordo (ligada por engano ou para corrigir) zera as
    /// DUAS chaves — é o escape que o Detach preserva mesmo com transbordo na carga.
    /// </summary>
    [Fact]
    public async Task Detach_ClearsTheTransshipmentKey()
    {
        var load = Load(ShipmentLoadStatus.Open);
        var transshipment = Transshipment(load, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        await _db.Context.SaveChangesAsync();

        var exit = Shipment(
            "R2", warehouseCode: "ARM99",
            shipmentLoadKey: load.Key, shipmentLoadTransshipmentKey: transshipment.Key);
        await _db.Context.SaveChangesAsync();

        await DetachService().ExecuteAsync(load.Key, [exit.Key], "tester");

        var saved = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == exit.Key);
        Assert.Null(saved.ShipmentLoadKey);
        Assert.Null(saved.ShipmentLoadTransshipmentKey);
    }

    /// <summary>
    /// A saída da ORIGEM (a que continua sem <c>ShipmentLoadTransshipmentKey</c>) não pode ser
    /// desvinculada enquanto a carga tiver transbordo: é dela que o rateio das liberações do
    /// transbordo depende. Sem esta trava o teste passaria com a carga ficando sem rastro do
    /// romaneio que originou o transbordo.
    /// </summary>
    [Fact]
    public async Task Detach_RefusesTheOriginExitWhileThereIsATransshipment()
    {
        var load = Load(ShipmentLoadStatus.InTransshipment);
        Transshipment(load, warehouseCode: "ARM99", entryStorageTransactionKey: Guid.NewGuid());
        var origin = Shipment("R1", warehouseCode: "ARM01", shipmentLoadKey: load.Key);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => DetachService().ExecuteAsync(load.Key, [origin.Key], "tester"));

        // Revisão da Task 7: mensagem reescrita para nomear o romaneio e a carga com clareza,
        // em vez das três frases curtas encadeadas de antes.
        Assert.Equal(
            "O romaneio R1 é a saída de origem da carga CG000001, que tem transbordo. As " +
            "liberações do transbordo dependem dele — estorne o transbordo antes de desvinculá-lo.",
            error.Message);
        Assert.Equal(
            load.Key,
            (await _db.Context.StorageTransactions.SingleAsync(x => x.Key == origin.Key)).ShipmentLoadKey);
    }

    // ─────────────────────────────── Cancel / Delete ───────────────────────────────

    /// <summary>
    /// Cancelar com transbordo aberto zeraria o saldo por baixo de mercadoria que já está fora da
    /// carga, num armazém intermediário sem nenhum documento que a rastreie até lá.
    /// </summary>
    [Fact]
    public async Task Cancel_RefusesWhileThereIsATransshipment()
    {
        var load = Load(ShipmentLoadStatus.InTransshipment, totalQuantity: 30_000m);
        Transshipment(load, warehouseCode: "ARM99", outgoingQuantity: 30_000m);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => CancelService().ExecuteAsync(load.Key, "Erro de montagem", "tester"));

        Assert.Contains("CG000001", error.Message);
        Assert.Contains("transbordo", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ShipmentLoadStatus.InTransshipment,
            (await _db.Context.ShipmentLoads.SingleAsync()).Status);
    }

    /// <summary>
    /// Defesa em profundidade (o guard de exclusão já barra fora do planejamento e sem romaneio):
    /// uma carga <c>Planned</c> com uma linha de transbordo — estado hoje inalcançável pelo app,
    /// mas reproduzível no InMemory — não pode ser excluída sem levar o transbordo com ela.
    /// </summary>
    [Fact]
    public async Task Delete_RefusesWhileThereIsATransshipment()
    {
        var load = Load(ShipmentLoadStatus.Planned, totalQuantity: 0m);
        Transshipment(load, warehouseCode: "ARM99", outgoingQuantity: 30_000m);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => DeleteService().ExecuteAsync(load.Key));

        Assert.Contains("transbordo", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_db.Context.ShipmentLoads);
        Assert.Single(_db.Context.ShipmentLoadsTransshipments);
    }

    // ─────────────────────────────── Leitura ───────────────────────────────

    /// <summary>
    /// A entrada do transbordo não carrega <c>ShipmentLoadKey</c> (a carga a alcança só por
    /// <c>ShipmentLoadTransshipmentKey</c>) — sem a subconsulta pelas chaves dos transbordos da
    /// carga, ela nunca apareceria no grid.
    /// </summary>
    [Fact]
    public async Task QueryTransactions_IncludesTheTransshipmentEntry()
    {
        var loadKey = Guid.NewGuid();
        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = loadKey,
            Sequence = 1,
            WarehouseCode = "ARM99",
            OutgoingQuantity = 30_000m,
        };
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.Context.SaveChangesAsync();

        var entry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0015",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            GrossWeight = 29_800m,
            NetWeight = 29_800m,
            ShipmentLoadTransshipmentKey = transshipment.Key,
        };
        _db.Context.StorageTransactions.Add(entry);
        await _db.Context.SaveChangesAsync();

        var result = await GetService().QueryTransactions(loadKey).ToListAsync();

        Assert.Contains(result, x => x.Key == entry.Key);
    }

    /// <summary>
    /// Regressão: a subconsulta é por transbordo DESTA carga — a entrada de um transbordo de
    /// OUTRA carga não pode vazar para este grid.
    /// </summary>
    [Fact]
    public async Task QueryTransactions_DoesNotIncludeAnotherLoadsTransshipmentEntry()
    {
        var loadKey = Guid.NewGuid();
        var otherLoadKey = Guid.NewGuid();
        var otherTransshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = otherLoadKey,
            Sequence = 1,
            WarehouseCode = "ARM99",
            OutgoingQuantity = 30_000m,
        };
        _db.Context.ShipmentLoadsTransshipments.Add(otherTransshipment);
        await _db.Context.SaveChangesAsync();

        var entry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0015",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            GrossWeight = 29_800m,
            NetWeight = 29_800m,
            ShipmentLoadTransshipmentKey = otherTransshipment.Key,
        };
        _db.Context.StorageTransactions.Add(entry);
        await _db.Context.SaveChangesAsync();

        var result = await GetService().QueryTransactions(loadKey).ToListAsync();

        Assert.DoesNotContain(result, x => x.Key == entry.Key);
    }
}
