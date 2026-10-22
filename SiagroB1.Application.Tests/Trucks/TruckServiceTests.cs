using Microsoft.EntityFrameworkCore;

using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Trucks;

public class TruckServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private TruckService Service(string erp = "STANDALONE") => new(_db.Context, TestConfiguration.Erp(erp), new TestLogger<TruckService>());

    [Fact]
    public async Task Trailer_with_wheel_type_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(
            new Truck { Code = "CAR1A11", Model = "Randon", Kind = TruckKind.Trailer, WheelType = TruckWheelType.TractorUnit }));

        Assert.Equal("Reboque não tem tipo de rodado: o rodado é só do veículo de tração.", ex.Message);
    }

    [Fact]
    public async Task Negative_capacity_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(
            new Truck { Code = "CAR1A11", Model = "Randon", CapacityKg = -1 }));

        Assert.Equal("Tara e capacidade não podem ser negativas.", ex.Message);
    }

    [Fact]
    public async Task Invalid_renavam_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(
            new Truck { Code = "CAR1A11", Model = "Randon", Renavam = "12AB" }));

        Assert.Equal("O RENAVAM tem de 9 a 11 dígitos.", ex.Message);
    }

    [Fact]
    public async Task Third_party_without_owner_is_rejected_in_standalone()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(
            new Truck { Code = "CAR1A11", Model = "Volvo", OwnerType = TruckOwnerType.ThirdParty }));

        Assert.Equal("Informe o parceiro proprietário do veículo de terceiro.", ex.Message);
    }

    [Fact]
    public async Task Invalid_owner_rntrc_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(
            new Truck { Code = "CAR1A11", Model = "Volvo", OwnerType = TruckOwnerType.ThirdParty, OwnerCardCode = "F00001", OwnerRntrc = "123" }));

        Assert.Equal("O RNTRC tem 8 dígitos.", ex.Message);
    }

    [Fact]
    public async Task Third_party_fields_are_cleared_in_sapb1()
    {
        await Service("SAPB1").CreateAsync(new Truck
        {
            Code = "CAR1A11", Model = "Volvo", OwnerType = TruckOwnerType.ThirdParty,
            OwnerCardCode = "F00001", OwnerRntrc = "12345678", OwnerKind = TruckOwnerKind.TacAggregate,
        });
        _db.Context.ChangeTracker.Clear();

        var saved = await _db.Context.Trucks.SingleAsync(x => x.Code == "CAR1A11");
        Assert.Equal(TruckOwnerType.Own, saved.OwnerType);
        Assert.Null(saved.OwnerCardCode);
        Assert.Null(saved.OwnerRntrc);
        Assert.Null(saved.OwnerKind);
    }

    [Fact]
    public async Task Update_with_trailer_wheel_type_is_rejected()
    {
        await Service().CreateAsync(new Truck { Code = "CAR1A11", Model = "Randon", Kind = TruckKind.Trailer });
        _db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DefaultException>(() => Service().UpdateAsync("CAR1A11",
            new Truck { Code = "CAR1A11", Model = "Randon", Kind = TruckKind.Trailer, WheelType = TruckWheelType.Truck }));
    }

    [Fact]
    public async Task Legacy_truck_without_new_fields_updates()
    {
        _db.Context.Trucks.Add(new Truck { Code = "OLD1A11", Model = "Antigo" });
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var updated = await Service().UpdateAsync("OLD1A11", new Truck { Code = "OLD1A11", Model = "Antigo 2" });

        Assert.Equal("Antigo 2", updated!.Model);
    }

    [Fact]
    public async Task CreateAsync_StoresThePlateNormalized()
    {
        var created = await Service().CreateAsync(new Truck { Code = "cud 1h57", Model = "Scania" });

        Assert.Equal("CUD1H57", created.Code);
        Assert.True(await _db.Context.Trucks.AnyAsync(x => x.Code == "CUD1H57"));
    }

    [Fact]
    public async Task CreateAsync_RejectsAnInvalidPlateWithoutSaving()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => Service().CreateAsync(new Truck { Code = "CUD 1H5", Model = "Scania" }));

        Assert.Equal("Placa inválida. Use o formato ABC1234 ou ABC1D23.", ex.Message);
        Assert.Empty(await _db.Context.Trucks.ToListAsync());
    }

    /// <summary>
    /// O caso do GAC-1190: "CUD 1H57" digitado com espaço quando CUD1H57 já existe. Depois de
    /// normalizada é a mesma placa, e o cadastro tem de ser recusado com mensagem clara, não com o
    /// erro genérico de chave duplicada.
    /// </summary>
    [Fact]
    public async Task CreateAsync_RejectsAPlateThatIsAlreadyRegisteredOnceNormalized()
    {
        _db.Context.Trucks.Add(new Truck { Code = "CUD1H57", Model = "Scania" });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => Service().CreateAsync(new Truck { Code = "CUD 1H57", Model = "Scania" }));

        Assert.Equal("Veículo CUD1H57 já cadastrado.", ex.Message);
        Assert.Equal(1, await _db.Context.Trucks.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_KeepsTheOtherFieldsEditable()
    {
        _db.Context.Trucks.Add(new Truck { Code = "CUD1H57", Model = "Scania" });
        await _db.Context.SaveChangesAsync();

        var truck = await _db.Context.Trucks.SingleAsync();
        truck.Model = "Volvo";
        truck.TareWeight = 15000;

        await Service().UpdateAsync("CUD1H57", truck);

        var stored = await _db.Context.Trucks.AsNoTracking().SingleAsync();
        Assert.Equal("Volvo", stored.Model);
        Assert.Equal(15000, stored.TareWeight);
    }

    /// <summary>
    /// A placa é a chave do veículo. Trocá-la na edição estourava 500 no EF; agora é recusada com
    /// mensagem de negócio.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_RejectsChangingThePlate()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => Service().UpdateAsync("CUD1H57", new Truck { Code = "CUD1H58", Model = "Scania" }));

        Assert.Equal("A placa do veículo não pode ser alterada.", ex.Message);
    }
}
