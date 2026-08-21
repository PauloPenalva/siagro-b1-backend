using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Montagem de Carga. Cobre a invariante I1 — "um romaneio, uma carga" — e a regra de
/// aglutinação (mesmo veículo, produto e filial).
/// </summary>
public class ShipmentLoadsCreateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsCreateService Service() => new(
        _db,
        new FakeDocNumberSequenceService(),
        new ShipmentLoadsMovementLogService(_db.Context));

    private StorageTransaction Shipment(
        string code,
        decimal grossWeight = 30_000,
        string truckCode = "ABC1D23",
        string itemCode = "SOJA",
        string branchCode = "01",
        StorageTransactionType type = StorageTransactionType.SalesShipment,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        Guid? shipmentLoadKey = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C001",
            ItemCode = itemCode,
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            WarehouseName = "ARMAZEM 01",
            BranchCode = branchCode,
            TruckCode = truckCode,
            TruckDriverCode = "M001",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = type,
            TransactionStatus = status,
            ShipmentLoadKey = shipmentLoadKey,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    [Fact]
    public async Task Assembles_three_homogeneous_shipments_into_one_load()
    {
        var a = Shipment("R1", 30_000);
        var b = Shipment("R2", 31_500);
        var c = Shipment("R3", 28_500);
        await _db.Context.SaveChangesAsync();

        var load = await Service().ExecuteAsync([a.Key, b.Key, c.Key], "Carga do dia", "tester");

        Assert.False(string.IsNullOrWhiteSpace(load.Code));
        Assert.Equal(ShipmentLoadStatus.Open, load.Status);
        // Soma o BRUTO, e não o líquido: é o número que hoje vira a quantidade da nota.
        Assert.Equal(90_000m, load.TotalQuantity);
        Assert.Equal(decimal.Zero, load.InvoicedQuantity);
        Assert.Equal(90_000m, load.AvailableQuantity);
        Assert.Equal("SOJA", load.ItemCode);
        Assert.Equal("ABC1D23", load.TruckCode);
        Assert.Equal("01", load.BranchCode);
        Assert.Equal("Carga do dia", load.Comments);

        var linked = await _db.Context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == load.Key)
            .Select(x => x.Code)
            .ToListAsync();

        Assert.Equal(3, linked.Count);
    }

    [Fact]
    public async Task Assembling_records_an_Assembled_movement()
    {
        var a = Shipment("R1", 30_000);
        await _db.Context.SaveChangesAsync();

        var load = await Service().ExecuteAsync([a.Key], null, "tester");

        var movement = await _db.Context.ShipmentLoadMovements
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.Assembled, movement.MovementType);
        // Montar não consome nada: o movimento é narrativa, com saldo cheio depois dele.
        Assert.Equal(decimal.Zero, movement.Quantity);
        Assert.Equal(30_000m, movement.BalanceAfter);
        Assert.Equal("tester", movement.CreatedBy);
    }

    [Fact]
    public async Task Refuses_an_empty_selection()
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([], null, "tester"));

        Assert.Contains("romaneio", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_shipments_from_different_trucks()
    {
        var a = Shipment("R1", truckCode: "ABC1D23");
        var b = Shipment("R2", truckCode: "XYZ9W87");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([a.Key, b.Key], null, "tester"));

        Assert.Contains("veículo", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_db.Context.ShipmentLoads);
    }

    [Fact]
    public async Task Refuses_shipments_of_different_items()
    {
        var a = Shipment("R1", itemCode: "SOJA");
        var b = Shipment("R2", itemCode: "MILHO");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([a.Key, b.Key], null, "tester"));

        Assert.Contains("produto", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_shipments_of_different_branches()
    {
        var a = Shipment("R1", branchCode: "01");
        var b = Shipment("R2", branchCode: "02");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([a.Key, b.Key], null, "tester"));

        Assert.Contains("filial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_shipment_already_in_another_load_naming_both_codes()
    {
        var existing = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000042",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
        };
        _db.Context.ShipmentLoads.Add(existing);

        var a = Shipment("R1", shipmentLoadKey: existing.Key);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([a.Key], null, "tester"));

        // Nomear os dois códigos é o que torna o erro acionável: o usuário precisa saber
        // QUAL romaneio e em QUAL carga ele já está.
        Assert.Contains("R1", error.Message);
        Assert.Contains("CG000042", error.Message);
    }

    [Fact]
    public async Task Refuses_a_shipment_that_is_not_confirmed()
    {
        var a = Shipment("R1", status: StorageTransactionsStatus.Pending);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([a.Key], null, "tester"));

        Assert.Contains("R1", error.Message);
    }

    [Fact]
    public async Task Refuses_a_transaction_that_is_not_a_shipment()
    {
        var a = Shipment("R1", type: StorageTransactionType.Purchase);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([a.Key], null, "tester"));

        Assert.Contains("R1", error.Message);
    }

    [Fact]
    public async Task Refuses_a_key_that_does_not_exist()
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync([Guid.NewGuid()], null, "tester"));

        Assert.Contains("não encontrado", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
