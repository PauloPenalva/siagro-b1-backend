using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.ShipmentLoads.ShipmentLoadsTransferTestData;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Os vizinhos da transferência entre armazéns de terceiros (GAC-1176): quem inicia, quem estorna,
/// quem vincula saída e quem fala com a carga Concluída.
/// </summary>
/// <remarks>
/// <c>Completed</c> numa carga Normal passou a ter DOIS significados — concluída pela Conferência
/// de Entregas (GAC-1171) ou por transferência. As dicas antigas mandavam "cancelar os documentos
/// de saída", que na transferência não existem: o usuário ficaria procurando uma nota para cancelar.
/// </remarks>
public class ShipmentLoadsTransferGuardsTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsMovementLogService MovementLog() => new(_db.Context);
    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);

    private ShipmentLoadsTransshipmentCompleteService CompleteTransferService() =>
        new(_db, MovementLog(), ChangeLog());

    private ShipmentLoadsTransshipmentStartService StartService() => new(
        _db,
        new FakeWarehouseService(new Dictionary<string, string> { [DestinationWarehouse] = "ARMAZEM DESTINO" }),
        MovementLog(),
        new WarehouseComplementService(_db));

    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment)> SeedCompletedByTransferAsync()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load, type: TransshipmentType.Transfer);
        await RegisterThirdPartyEntryAsync(_db, transshipment);
        await CompleteTransferService().ExecuteAsync(transshipment.Key!.Value, "tester");
        return (load, transshipment);
    }

    private StorageTransaction UnlinkedExit()
    {
        var exit = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R-SAIDA",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = DestinationWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 29_800m,
            NetWeight = 29_800m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(exit);
        return exit;
    }

    // ─── Dicas da carga Concluída ───

    [Fact]
    public void The_hints_of_a_load_completed_by_transfer_point_to_reopening_the_transfer()
    {
        var load = new ShipmentLoad { Code = "CG1", ItemCode = "SOJA", UnitOfMeasureCode = "KG" };

        Assert.Equal("Reabra a transferência", ShipmentLoadCompletionRules.UndoHint(load, completedByTransfer: true));
        Assert.Equal("Reabra a transferência", ShipmentLoadCompletionRules.CompositionHint(load, completedByTransfer: true));
    }

    [Fact]
    public async Task Cancelling_a_load_completed_by_transfer_points_to_reopening_the_transfer()
    {
        var (load, _) = await SeedCompletedByTransferAsync();

        var service = new ShipmentLoadsCancelService(
            _db, new ShipmentLoadsCompositionGuardService(_db.Context), MovementLog(), ChangeLog());

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(load.Key, "engano", "tester"));

        Assert.Contains("Reabra a transferência", ex.Message);
        Assert.DoesNotContain("documentos de saída", ex.Message);
    }

    [Fact]
    public async Task Detaching_from_a_load_completed_by_transfer_points_to_reopening_the_transfer()
    {
        var (load, _) = await SeedCompletedByTransferAsync();
        var origin = await _db.Context.StorageTransactions.SingleAsync(x => x.ShipmentLoadKey == load.Key);

        var service = new ShipmentLoadsDetachTransactionsService(
            _db, new ShipmentLoadsCompositionGuardService(_db.Context), MovementLog());

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(load.Key, [origin.Key], "tester"));

        Assert.Contains("Reabra a transferência", ex.Message);
    }

    [Fact]
    public async Task Attaching_to_a_load_completed_by_transfer_points_to_reopening_the_transfer()
    {
        var (load, _) = await SeedCompletedByTransferAsync();
        var exit = UnlinkedExit();
        await _db.Context.SaveChangesAsync();

        var service = new ShipmentLoadsAttachTransactionsService(_db, MovementLog());

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(load.Key, [exit.Key], null, "tester"));

        Assert.Contains("Reabra a transferência", ex.Message);
    }

    /// <summary>O "Reabrir Carga" é da Remoção: a carga de transferência reabre pelo transbordo.</summary>
    [Fact]
    public async Task Reopening_the_load_itself_points_to_the_transfer_action()
    {
        var (load, _) = await SeedCompletedByTransferAsync();

        var service = new ShipmentLoadsReopenService(_db, MovementLog(), ChangeLog());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(load.Key, "tester"));

        Assert.Contains("Reabrir Transferência", ex.Message);
        Assert.DoesNotContain("conferência", ex.Message);
    }

    /// <summary>O "Concluir Carga" é da Remoção: na carga em transbordo o caminho é outro botão.</summary>
    [Fact]
    public async Task Completing_a_normal_load_in_transshipment_points_to_the_transfer_action()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load);
        await RegisterThirdPartyEntryAsync(_db, transshipment);

        var service = new ShipmentLoadsCompleteService(_db, MovementLog(), ChangeLog());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(load.Key, "tester"));

        Assert.Contains("Concluir Transferência", ex.Message);
    }

    // ─── Estorno e vínculo de saída ───

    [Fact]
    public async Task Reversing_a_completed_transfer_asks_to_reopen_it_first()
    {
        var (load, transshipment) = await SeedCompletedByTransferAsync();

        var service = new ShipmentLoadsTransshipmentReverseService(_db, MovementLog());

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transshipment.Key!.Value, null, "tester"));

        Assert.Contains("Reabra a transferência", ex.Message);
        Assert.NotEmpty(await _db.Context.ShipmentLoadsTransshipments
            .AsNoTracking().Where(x => x.ShipmentLoadKey == load.Key).ToListAsync());
    }

    /// <summary>
    /// A transferência TERMINA no armazém: vincular uma Expedição como saída dela a transformaria
    /// num transbordo comum pela porta dos fundos, com a tela ainda dizendo "Transferência".
    /// </summary>
    [Fact]
    public async Task A_transfer_does_not_accept_a_linked_exit()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load, type: TransshipmentType.Transfer);
        await RegisterThirdPartyEntryAsync(_db, transshipment);
        var exit = UnlinkedExit();
        await _db.Context.SaveChangesAsync();

        var service = new ShipmentLoadsAttachTransactionsService(_db, MovementLog());

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(load.Key, [exit.Key], transshipment.Key, "tester"));

        Assert.Contains("é uma transferência", ex.Message);
    }

    // ─── Iniciar como Transferência ───

    private ShipmentLoad OpenLoad()
    {
        var load = Load(_db);
        load.Status = ShipmentLoadStatus.Open;
        load.TransshippedQuantity = decimal.Zero;
        return load;
    }

    [Fact]
    public async Task Starting_as_a_transfer_marks_the_row_and_the_load()
    {
        var load = OpenLoad();
        await _db.Context.SaveChangesAsync();

        var transshipment = await StartService().ExecuteAsync(
            load.Key, DestinationWarehouse, DateTime.Today, null, "tester", TransshipmentType.Transfer);

        Assert.Equal(TransshipmentType.Transfer, transshipment.Type);

        var saved = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.InTransshipment, saved.Status);
        Assert.True(saved.IsInTransfer);

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking()
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);
        Assert.StartsWith("Transferência 1 iniciada", movement.Description);
    }

    [Fact]
    public async Task Starting_without_a_type_is_still_a_plain_transshipment()
    {
        var load = OpenLoad();
        await _db.Context.SaveChangesAsync();

        var transshipment = await StartService().ExecuteAsync(
            load.Key, DestinationWarehouse, DateTime.Today, null, "tester");

        Assert.Equal(TransshipmentType.Transshipment, transshipment.Type);
        Assert.False((await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).IsInTransfer);
    }

    /// <summary>
    /// Mercadoria que fica em armazém PRÓPRIO é Entrada em Armazenagem. Recusar já no início evita
    /// que o operador descubra só na hora de concluir que escolheu o processo errado.
    /// </summary>
    [Fact]
    public async Task A_transfer_to_an_own_warehouse_is_refused_at_the_start()
    {
        var load = OpenLoad();
        _db.Context.WarehouseComplements.Add(new WarehouseComplement
        {
            WarehouseCode = DestinationWarehouse,
            IsOwn = true,
        });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => StartService().ExecuteAsync(
            load.Key, DestinationWarehouse, DateTime.Today, null, "tester", TransshipmentType.Transfer));

        Assert.Contains("armazém próprio", ex.Message);
        Assert.Empty(await _db.Context.ShipmentLoadsTransshipments.AsNoTracking().ToListAsync());
    }
}
