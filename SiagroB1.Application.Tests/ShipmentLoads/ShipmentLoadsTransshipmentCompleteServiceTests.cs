using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.ShipmentLoads.ShipmentLoadsTransferTestData;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Concluir a transferência entre armazéns de terceiros (GAC-1176): a carga que descarregou num
/// armazém de terceiro e termina ali encerra SEM faturamento.
/// </summary>
/// <remarks>
/// Antes deste serviço a carga ficava em "Em transbordo" para sempre: o transbordo só fechava com
/// uma Expedição de venda vinculada como saída, que numa transferência nunca existe.
/// </remarks>
public class ShipmentLoadsTransshipmentCompleteServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsTransshipmentCompleteService Service() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

    private Task<ShipmentLoad> LoadAsync(Guid key) =>
        _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<ShipmentLoadTransshipment> SavedAsync(Guid key) =>
        _db.Context.ShipmentLoadsTransshipments.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Complete_ClosesTheLoadWithoutInvoicing()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load, type: TransshipmentType.Transfer);
        await RegisterThirdPartyEntryAsync(_db, transshipment);

        await Service().ExecuteAsync(transshipment.Key!.Value, "tester");

        var saved = await LoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.Completed, saved.Status);
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.False(saved.IsInTransfer);

        var savedTransshipment = await SavedAsync(transshipment.Key!.Value);
        Assert.NotNull(savedTransshipment.FinalDestinationAt);
        Assert.Equal("tester", savedTransshipment.FinalDestinationBy);
    }

    /// <summary>
    /// A mercadoria fica disponível no armazém de destino: o romaneio de entrada e a liberação que
    /// ele gerou continuam vivos, para a Expedição de Grãos tirá-la dali noutra carga.
    /// </summary>
    [Fact]
    public async Task Complete_KeepsTheEntryAndItsReleaseAlive()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load);
        var (entry, release) = await RegisterThirdPartyEntryAsync(_db, transshipment);

        await Service().ExecuteAsync(transshipment.Key!.Value, "tester");

        Assert.Equal(
            StorageTransactionsStatus.Confirmed,
            (await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == entry.Key)).TransactionStatus);
        Assert.Equal(
            ReleaseStatus.Actived,
            (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key)).Status);
    }

    /// <summary>O plano mudou no meio do caminho: o transbordo comum concluído aqui VIRA transferência.</summary>
    [Fact]
    public async Task Complete_ConvertsAPlainTransshipmentIntoATransfer()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load, type: TransshipmentType.Transshipment);
        await RegisterThirdPartyEntryAsync(_db, transshipment);

        await Service().ExecuteAsync(transshipment.Key!.Value, "tester");

        Assert.Equal(TransshipmentType.Transfer, (await SavedAsync(transshipment.Key!.Value)).Type);
    }

    [Fact]
    public async Task Complete_RegistersTheMovementWithEntryAndShrinkage()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load, outgoing: 30_000m);
        await RegisterThirdPartyEntryAsync(_db, transshipment, entryQuantity: 29_800m);

        await Service().ExecuteAsync(transshipment.Key!.Value, "tester");

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking()
            .SingleAsync(x => x.ShipmentLoadKey == load.Key &&
                              x.MovementType == ShipmentLoadMovementType.TransferCompleted);

        Assert.Contains("ARM99", movement.Description);
        Assert.Contains($"{29_800m:N3}", movement.Description);
        Assert.Contains($"{200m:N3}", movement.Description);
    }

    [Fact]
    public async Task Complete_RefusesBeforeTheEntryIsRegistered()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("ainda não foi registrada", ex.Message);
        Assert.Null((await SavedAsync(transshipment.Key!.Value)).FinalDestinationAt);
    }

    /// <summary>
    /// Armazém PRÓPRIO não é destino de transferência: quem recebe ali é a Entrada em
    /// Armazenagem. O discriminador é a estrutura persistida (entrada = Receipt), não IsOwn ao vivo.
    /// </summary>
    [Fact]
    public async Task Complete_RefusesAnOwnWarehouse()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load);
        await RegisterOwnWarehouseEntryAsync(_db, transshipment);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("armazém próprio", ex.Message);
    }

    [Fact]
    public async Task Complete_RefusesWhenTheExitIsAlreadyLinked()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load);
        await RegisterThirdPartyEntryAsync(_db, transshipment);
        await LinkExitAsync(_db, load, transshipment);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("Expedição de saída", ex.Message);
    }

    [Fact]
    public async Task Complete_RefusesATransshipmentThatIsNotTheLast()
    {
        var load = Load(_db, totalQuantity: 60_000m);
        var first = await TransshipmentAsync(_db, load, sequence: 1);
        await RegisterThirdPartyEntryAsync(_db, first);
        await TransshipmentAsync(_db, load, sequence: 2);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(first.Key!.Value, "tester"));

        Assert.Contains("último", ex.Message);
    }

    [Fact]
    public async Task Complete_RefusesTwice()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load);
        await RegisterThirdPartyEntryAsync(_db, transshipment);
        await Service().ExecuteAsync(transshipment.Key!.Value, "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("já foi concluída", ex.Message);
    }

    [Fact]
    public async Task Complete_RefusesALoadWithAPendingRefusal()
    {
        var load = Load(_db);
        load.Status = ShipmentLoadStatus.RefusalPending;
        var transshipment = await TransshipmentAsync(_db, load);
        await RegisterThirdPartyEntryAsync(_db, transshipment);

        await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.DefaultException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Null((await SavedAsync(transshipment.Key!.Value)).FinalDestinationAt);
    }
}
