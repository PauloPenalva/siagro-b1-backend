using Microsoft.EntityFrameworkCore;

using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Trucks;

public class TruckServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private TruckService Service() => new(_db.Context, new TestLogger<TruckService>());

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
