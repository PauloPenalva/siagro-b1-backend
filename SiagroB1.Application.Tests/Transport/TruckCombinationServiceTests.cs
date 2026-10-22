using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Transport;

public class TruckCombinationServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private TruckCombinationService Service(string erp = "STANDALONE") => new(_db, TestConfiguration.Erp(erp));

    private async Task SeedAsync()
    {
        _db.Context.Trucks.AddRange(
            new Truck { Code = "CAV1A11", Model = "Scania", Kind = TruckKind.Traction },
            new Truck { Code = "CAV2B22", Model = "Volvo", Kind = TruckKind.Traction, Inactive = true },
            new Truck { Code = "CAR1C33", Model = "Randon", Kind = TruckKind.Trailer },
            new Truck { Code = "CAR2D44", Model = "Randon", Kind = TruckKind.Trailer },
            new Truck { Code = "CAR3E55", Model = "Randon", Kind = TruckKind.Trailer },
            new Truck { Code = "CAR4F66", Model = "Randon", Kind = TruckKind.Trailer });
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
    }

    private static TruckCombinationInput Input(string traction, params string[] trailers) =>
        new("BITREM01", "Bitrem graneleiro", traction, null, false, trailers);

    [Fact]
    public async Task Creates_combination_with_trailers_in_order()
    {
        await SeedAsync();

        await Service().SaveAsync(Input("CAV1A11", "CAR2D44", "CAR1C33"), isNew: true);
        _db.Context.ChangeTracker.Clear();

        var saved = await _db.Context.TruckCombinations.Include(x => x.Trailers).SingleAsync();
        Assert.Equal("CAV1A11", saved.TractionTruckCode);
        Assert.Equal(["CAR2D44", "CAR1C33"], saved.Trailers.OrderBy(t => t.Position).Select(t => t.TrailerTruckCode));
        Assert.Equal([1, 2], saved.Trailers.OrderBy(t => t.Position).Select(t => t.Position));
    }

    [Fact]
    public async Task Combination_requires_traction()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync(Input("CAR1C33"), isNew: true));

        Assert.Equal("Escolha um veículo de tração para o conjunto.", ex.Message);
    }

    [Fact]
    public async Task Trailer_must_be_trailer_kind()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync(Input("CAV1A11", "CAV1A11"), isNew: true));

        Assert.Equal("O veículo CAV1A11 não é reboque.", ex.Message);
    }

    [Fact]
    public async Task More_than_three_trailers_is_rejected()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input("CAV1A11", "CAR1C33", "CAR2D44", "CAR3E55", "CAR4F66"), isNew: true));

        Assert.Equal("O conjunto aceita no máximo 3 reboques.", ex.Message);
    }

    [Fact]
    public async Task Duplicate_trailer_is_rejected()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync(Input("CAV1A11", "CAR1C33", "CAR1C33"), isNew: true));

        Assert.Equal("O reboque CAR1C33 aparece mais de uma vez.", ex.Message);
    }

    [Fact]
    public async Task Inactive_vehicle_is_rejected()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync(Input("CAV2B22", "CAR1C33"), isNew: true));

        Assert.Equal("O veículo CAV2B22 está inativo.", ex.Message);
    }

    [Fact]
    public async Task Inactivating_combination_with_inactive_vehicle_is_allowed()
    {
        await SeedAsync();
        await Service().SaveAsync(Input("CAV1A11", "CAR1C33"), isNew: true);
        var traction = await _db.Context.Trucks.SingleAsync(x => x.Code == "CAV1A11");
        traction.Inactive = true;
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        await Service().SaveAsync(Input("CAV1A11", "CAR1C33") with { Inactive = true }, isNew: false);

        Assert.True((await _db.Context.TruckCombinations.SingleAsync()).Inactive);
    }

    [Fact]
    public async Task Same_trailer_in_two_combinations_is_allowed()
    {
        await SeedAsync();

        await Service().SaveAsync(Input("CAV1A11", "CAR1C33"), isNew: true);
        await Service().SaveAsync(Input("CAV1A11", "CAR1C33") with { Code = "BITREM02" }, isNew: true);

        Assert.Equal(2, await _db.Context.TruckCombinations.CountAsync());
    }

    [Fact]
    public async Task Duplicate_code_is_rejected_on_create()
    {
        await SeedAsync();
        await Service().SaveAsync(Input("CAV1A11"), isNew: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync(Input("CAV1A11"), isNew: true));

        Assert.Equal("Conjunto BITREM01 já cadastrado.", ex.Message);
    }

    [Fact]
    public async Task Update_replaces_trailers_and_renumbers_positions()
    {
        await SeedAsync();
        await Service().SaveAsync(Input("CAV1A11", "CAR1C33", "CAR2D44"), isNew: true);
        _db.Context.ChangeTracker.Clear();

        await Service().SaveAsync(Input("CAV1A11", "CAR3E55") with { Description = "Carreta simples" }, isNew: false);
        _db.Context.ChangeTracker.Clear();

        var saved = await _db.Context.TruckCombinations.Include(x => x.Trailers).SingleAsync();
        Assert.Equal("Carreta simples", saved.Description);
        var trailer = Assert.Single(saved.Trailers);
        Assert.Equal("CAR3E55", trailer.TrailerTruckCode);
        Assert.Equal(1, trailer.Position);
        Assert.Equal(1, await _db.Context.TruckCombinationTrailers.CountAsync());
    }

    [Fact]
    public async Task Update_of_missing_combination_is_rejected()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync(Input("CAV1A11"), isNew: false));

        Assert.Equal("Conjunto BITREM01 não encontrado.", ex.Message);
    }

    [Fact]
    public async Task Rejected_in_sapb1()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").SaveAsync(Input("CAV1A11"), isNew: true));

        Assert.Equal("O conjunto só existe no modo STANDALONE.", ex.Message);
    }
}
