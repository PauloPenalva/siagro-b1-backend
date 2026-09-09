using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O log campo a campo da edição da carga, e a garantia de que a linha narrativa da
/// Movimentação continua exatamente como era.
/// </summary>
public class ShipmentLoadsChangeLogTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsUpdateService Service() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

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
        string? truckCode = "ABC1D23",
        string? driverName = "JOAO",
        decimal? freightPrice = 1_000m,
        bool hasExcess = false) => new()
    {
        Key = key,
        BranchCode = "01",
        LoadDate = new DateTime(2026, 8, 28),
        ItemCode = "SOJA",
        ItemName = "SOJA EM GRAOS",
        UnitOfMeasureCode = "KG",
        TruckCode = truckCode,
        TruckDriverName = driverName,
        WarehouseCode = "ARM01",
        FreightPrice = freightPrice,
        HasExcess = hasExcess,
    };

    private List<ShipmentLoadChangeLog> LogsOf(Guid loadKey) =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == loadKey).ToList();

    [Fact]
    public async Task Each_changed_field_gets_its_own_log_row()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(
            Input(load.Key, truckCode: "XYZ4E56", driverName: "PEDRO", freightPrice: 1_200m),
            "joao");

        var logs = LogsOf(load.Key);
        Assert.Equal(3, logs.Count);

        var truck = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.TruckCode);
        Assert.Equal("ABC1D23", truck.OldValue);
        Assert.Equal("XYZ4E56", truck.NewValue);

        var driver = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.TruckDriver);
        Assert.Equal("JOAO", driver.OldValue);
        Assert.Equal("PEDRO", driver.NewValue);

        var freight = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.FreightPrice);
        Assert.Equal("1.000,00", freight.OldValue);
        Assert.Equal("1.200,00", freight.NewValue);

        Assert.All(logs, l => Assert.Equal("joao", l.ChangedBy));

        // A mesma edição também tem que gerar UMA frase só na Movimentação, com os três itens
        // juntados por "; " — é o formato que a Logística lê e que o log não substitui.
        var movement = _db.Context.ShipmentLoadMovements
            .Single(m => m.ShipmentLoadKey == load.Key);

        Assert.Equal(
            "Dados da carga alterados: Veículo: 'ABC1D23' para 'XYZ4E56'; " +
            "Motorista: 'JOAO' para 'PEDRO'; Valor do frete: '1.000,00' para '1.200,00'.",
            movement.Description);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_writes_neither_log_nor_movement()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(Input(load.Key), "joao");

        Assert.Empty(LogsOf(load.Key));
        Assert.Empty(_db.Context.ShipmentLoadMovements.Where(m => m.ShipmentLoadKey == load.Key).ToList());
    }

    [Fact]
    public async Task The_movement_narrative_is_unchanged()
    {
        // Regressao: a Movimentacao e a linha do tempo que a Logistica le. O log acrescenta
        // granularidade, nao substitui a narrativa.
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(Input(load.Key, truckCode: "XYZ4E56"), "joao");

        var movement = _db.Context.ShipmentLoadMovements
            .Single(m => m.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.Updated, movement.MovementType);
        Assert.Equal(
            "Dados da carga alterados: Veículo: 'ABC1D23' para 'XYZ4E56'.",
            movement.Description);
    }

    [Fact]
    public async Task A_boolean_and_a_date_are_written_as_pt_br_text()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        var input = Input(load.Key, hasExcess: true);
        input.LoadDate = new DateTime(2026, 8, 30);

        await Service().ExecuteAsync(input, "joao");

        var logs = LogsOf(load.Key);

        var excess = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.HasExcess);
        Assert.Equal("Não", excess.OldValue);
        Assert.Equal("Sim", excess.NewValue);

        var date = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.LoadDate);
        Assert.Equal("28/08/2026", date.OldValue);
        Assert.Equal("30/08/2026", date.NewValue);
    }

    [Fact]
    public async Task The_movement_narrative_covers_unit_date_excess_and_freight_price()
    {
        // Regressao: Unidade, Data, Excesso e Valor do frete sao formatados por helpers
        // dedicados (DescribeDate/DescribeBoolean/DescribeFreightPrice) que vivem fora do
        // DescribeChanges. O log de cada um esta coberto acima; esta e a garantia equivalente
        // para a frase da Movimentacao, que e o texto que a Logistica realmente le.
        var load = Load();
        await _db.Context.SaveChangesAsync();

        var input = Input(load.Key, freightPrice: 1_200m, hasExcess: true);
        input.LoadDate = new DateTime(2026, 8, 30);
        input.UnitOfMeasureCode = "TON";

        await Service().ExecuteAsync(input, "joao");

        var movement = _db.Context.ShipmentLoadMovements
            .Single(m => m.ShipmentLoadKey == load.Key);

        // Ordem = ordem das chamadas de Compare em DescribeChanges: Unidade antes de Filial e
        // Observações (não alteradas aqui), depois Data, Excesso e Valor do frete.
        Assert.Equal(
            "Dados da carga alterados: Unidade: 'KG' para 'TON'; " +
            "Data: '28/08/2026' para '30/08/2026'; Excesso: 'Não' para 'Sim'; " +
            "Valor do frete: '1.000,00' para '1.200,00'.",
            movement.Description);
    }
}
