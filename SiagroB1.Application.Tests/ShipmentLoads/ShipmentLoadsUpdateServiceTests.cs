using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Edição da carga. A imutabilidade acompanha o que já virou documento fiscal, e não o status
/// por si: o caso real que motiva a regra é "o motorista trocou depois de carregar".
/// </summary>
public class ShipmentLoadsUpdateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsUpdateService Service() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoad Load(ShipmentLoadStatus status = ShipmentLoadStatus.Planned)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            LoadDate = new DateTime(2026, 8, 28),
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            TruckDriverName = "JOAO",
            WarehouseCode = "ARM01",
            Status = status,
            FreightPrice = 1_000m,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private static ShipmentLoad Input(
        Guid key,
        string itemCode = "SOJA",
        string unitOfMeasureCode = "KG",
        string? branchCode = "01",
        string? truckCode = "ABC1D23",
        string? driverName = "JOAO",
        decimal? freightPrice = 1_000m) => new()
    {
        Key = key,
        BranchCode = branchCode,
        LoadDate = new DateTime(2026, 8, 28),
        ItemCode = itemCode,
        ItemName = "SOJA EM GRAOS",
        UnitOfMeasureCode = unitOfMeasureCode,
        TruckCode = truckCode,
        TruckDriverName = driverName,
        WarehouseCode = "ARM01",
        FreightPrice = freightPrice,
    };

    private StorageTransaction Shipment(Guid loadKey, string code, string truckCode = "ABC1D23")
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            TruckCode = truckCode,
            GrossWeight = 30_000,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = loadKey,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Planned)]
    [InlineData(ShipmentLoadStatus.Open)]
    public async Task Everything_is_editable_before_billing(ShipmentLoadStatus status)
    {
        var load = Load(status);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(
            Input(load.Key, itemCode: "MILHO", unitOfMeasureCode: "TON", branchCode: "02"),
            "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal("MILHO", saved.ItemCode);
        Assert.Equal("TON", saved.UnitOfMeasureCode);
        Assert.Equal("02", saved.BranchCode);
    }

    /// <summary>
    /// Produto, unidade e filial já viraram linha de nota e não podem mais mudar.
    /// </summary>
    [Theory]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, "MILHO", "KG", "01", "produto")]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, "SOJA", "TON", "01", "unidade")]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, "SOJA", "KG", "02", "filial")]
    [InlineData(ShipmentLoadStatus.Invoiced, "MILHO", "KG", "01", "produto")]
    public async Task Refuses_to_change_fiscal_fields_after_billing(
        ShipmentLoadStatus status, string item, string uom, string branch, string expected)
    {
        var load = Load(status);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(
                Input(load.Key, itemCode: item, unitOfMeasureCode: uom, branchCode: branch),
                "tester"));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O caso real: o motorista trocou depois de carregar. Isso continua editável mesmo com a
    /// carga faturada — é o que impede o usuário de cancelar e refazer tudo.
    /// </summary>
    [Fact]
    public async Task Informative_fields_stay_editable_after_billing()
    {
        var load = Load(ShipmentLoadStatus.PartiallyInvoiced);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(
            Input(load.Key, driverName: "PEDRO", freightPrice: 3_000m), "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal("PEDRO", saved.TruckDriverName);
        Assert.Equal(3_000m, saved.FreightPrice);
    }

    [Fact]
    public async Task A_cancelled_load_is_frozen()
    {
        var load = Load(ShipmentLoadStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input(load.Key, driverName: "PEDRO"), "tester"));

        Assert.Contains("cancelada", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A placa é a chave de homogeneidade: trocá-la com romaneio de outra placa vinculado
    /// deixaria a carga inconsistente com a própria composição.
    /// </summary>
    [Fact]
    public async Task Refuses_to_change_the_truck_when_a_shipment_disagrees()
    {
        var load = Load(ShipmentLoadStatus.Open);
        Shipment(load.Key, "R1", "ABC1D23");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input(load.Key, truckCode: "XYZ9W87"), "tester"));

        Assert.Contains("R1", error.Message);
        Assert.Contains("ABC1D23", error.Message);
    }

    [Fact]
    public async Task Allows_changing_the_truck_of_an_empty_load()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(Input(load.Key, truckCode: "XYZ9W87"), "tester");

        Assert.Equal("XYZ9W87", (await _db.Context.ShipmentLoads.SingleAsync()).TruckCode);
    }

    [Fact]
    public async Task Editing_records_an_Updated_movement_naming_what_changed()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(Input(load.Key, driverName: "PEDRO"), "tester");

        var movement = await _db.Context.ShipmentLoadMovements
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.Updated, movement.MovementType);
        Assert.Contains("JOAO", movement.Description);
        Assert.Contains("PEDRO", movement.Description);
    }

    /// <summary>
    /// Gravar sem mudar nada não polui o histórico.
    /// </summary>
    [Fact]
    public async Task Saving_without_changes_records_no_movement()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(Input(load.Key), "tester");

        Assert.Empty(_db.Context.ShipmentLoadMovements);
    }

    [Fact]
    public async Task Refuses_a_form_missing_the_truck()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input(load.Key, truckCode: null), "tester"));

        Assert.Contains("placa", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
