using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Criação da carga pelo formulário da Logística — o planejamento, antes do carregamento.
/// </summary>
/// <remarks>
/// Os testes de homogeneidade e de elegibilidade de romaneio que moravam aqui migraram para
/// <see cref="ShipmentLoadsAttachTransactionsServiceTests"/>: a criação deixou de conhecer
/// romaneio. O que sobrou é o que o formulário exige e o estado em que a carga nasce.
/// </remarks>
public class ShipmentLoadsCreateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsCreateService Service() => new(
        _db,
        new FakeDocNumberSequenceService(),
        new ShipmentLoadsMovementLogService(_db.Context));

    private static ShipmentLoad Form(
        string? branchCode = "01",
        string? truckCode = "ABC1D23",
        string itemCode = "SOJA",
        string unitOfMeasureCode = "KG",
        string? warehouseCode = "ARM01",
        decimal? freightPrice = 2_500.50m) => new()
    {
        BranchCode = branchCode,
        LoadDate = new DateTime(2026, 8, 28),
        TruckCode = truckCode,
        TruckDriverCode = "M001",
        TruckDriverName = "JOAO MOTORISTA",
        CarrierCardCode = "T001",
        CarrierName = "TRANSPORTADORA TESTE",
        ItemCode = itemCode,
        ItemName = "SOJA EM GRAOS",
        UnitOfMeasureCode = unitOfMeasureCode,
        WarehouseCode = warehouseCode,
        WarehouseName = "ARMAZEM 01",
        CardCode = "C001",
        CardName = "CLIENTE TESTE",
        HasExcess = true,
        FreightPrice = freightPrice,
        Comments = "Carga do dia",
    };

    [Fact]
    public async Task A_new_load_is_born_planned_and_empty()
    {
        var load = await Service().ExecuteAsync(Form(), "tester");

        Assert.False(string.IsNullOrWhiteSpace(load.Code));
        // O par que define planejamento: sem volume, situação Planejada. Se este teste
        // ficar vermelho com Open, o ramo Planned de ResolveStatus foi removido.
        Assert.Equal(ShipmentLoadStatus.Planned, load.Status);
        Assert.Equal(decimal.Zero, load.TotalQuantity);
        Assert.Equal(decimal.Zero, load.InvoicedQuantity);
        Assert.Equal(decimal.Zero, load.AvailableQuantity);
    }

    [Fact]
    public async Task The_logistics_fields_are_persisted()
    {
        await Service().ExecuteAsync(Form(), "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();

        Assert.Equal("ABC1D23", saved.TruckCode);
        Assert.Equal("JOAO MOTORISTA", saved.TruckDriverName);
        Assert.Equal("T001", saved.CarrierCardCode);
        Assert.Equal("TRANSPORTADORA TESTE", saved.CarrierName);
        Assert.Equal("C001", saved.CardCode);
        Assert.Equal("CLIENTE TESTE", saved.CardName);
        Assert.True(saved.HasExcess);
        Assert.Equal(2_500.50m, saved.FreightPrice);
        Assert.Equal("Carga do dia", saved.Comments);
        Assert.Equal("tester", saved.CreatedBy);
    }

    [Fact]
    public async Task Creating_records_a_Planned_movement()
    {
        var load = await Service().ExecuteAsync(Form(), "tester");

        var movement = await _db.Context.ShipmentLoadMovements
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.Planned, movement.MovementType);
        // Planejar não move volume nenhum: o movimento é narrativa.
        Assert.Equal(decimal.Zero, movement.Quantity);
        Assert.Equal(decimal.Zero, movement.BalanceAfter);
        Assert.Equal("tester", movement.CreatedBy);
    }

    [Theory]
    [InlineData(null, "ABC1D23", "SOJA", "KG", "ARM01", "filial")]
    [InlineData("01", null, "SOJA", "KG", "ARM01", "placa")]
    [InlineData("01", "ABC1D23", "", "KG", "ARM01", "produto")]
    [InlineData("01", "ABC1D23", "SOJA", "", "ARM01", "unidade")]
    [InlineData("01", "ABC1D23", "SOJA", "KG", null, "armazém")]
    public async Task Refuses_a_form_missing_a_required_field(
        string? branch, string? truck, string item, string uom, string? warehouse, string expected)
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(
                Form(branchCode: branch, truckCode: truck, itemCode: item,
                     unitOfMeasureCode: uom, warehouseCode: warehouse),
                "tester"));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_db.Context.ShipmentLoads);
    }

    [Fact]
    public async Task Refuses_a_negative_freight_price()
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Form(freightPrice: -1m), "tester"));

        Assert.Contains("frete", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O frete é anulável de propósito, para distinguir "não informado" de "zero" — e a
    /// Logística nem sempre sabe o valor no momento do planejamento.
    /// </summary>
    [Fact]
    public async Task Accepts_a_form_without_a_freight_price()
    {
        var load = await Service().ExecuteAsync(Form(freightPrice: null), "tester");

        Assert.Null(load.FreightPrice);
    }
}
