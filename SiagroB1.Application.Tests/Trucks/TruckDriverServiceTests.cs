using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Trucks;

public class TruckDriverServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private TruckDriverService Service() => new(_db.Context, new TestLogger<TruckDriverService>());

    private static TruckDriver Driver(string code, string? category = null) =>
        new() { Code = code, Name = "João da Silva", Document = "RG 123", Phone = "", LicenseCategory = category };

    [Fact]
    public async Task Create_with_invalid_cpf_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(Driver("12345678900")));

        Assert.Equal("Informe um CPF válido no código do motorista.", ex.Message);
    }

    [Fact]
    public async Task Create_normalizes_masked_cpf()
    {
        var created = await Service().CreateAsync(Driver("529.982.247-25"));

        Assert.Equal("52998224725", created.Code);
        Assert.True(await _db.Context.TruckDrivers.AnyAsync(x => x.Code == "52998224725"));
    }

    [Fact]
    public async Task Create_duplicate_cpf_is_rejected()
    {
        await Service().CreateAsync(Driver("52998224725"));
        _db.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(Driver("529.982.247-25")));

        Assert.Equal("Motorista com CPF 52998224725 já cadastrado.", ex.Message);
    }

    [Fact]
    public async Task Update_keeps_legacy_non_cpf_code()
    {
        _db.Context.TruckDrivers.Add(Driver("MOT01"));
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var updated = await Service().UpdateAsync("MOT01", Driver("MOT01"));

        Assert.NotNull(updated);
    }

    [Theory]
    [InlineData("F")]
    [InlineData("ae")]
    [InlineData("ABC")]
    public async Task Invalid_license_category_is_rejected(string category)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateAsync(Driver("52998224725", category)));

        Assert.Equal("Categoria da CNH inválida.", ex.Message);
    }

    [Fact]
    public async Task Valid_license_category_is_accepted_on_update()
    {
        _db.Context.TruckDrivers.Add(Driver("MOT01"));
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var updated = await Service().UpdateAsync("MOT01", Driver("MOT01", "AE"));

        Assert.Equal("AE", updated!.LicenseCategory);
    }
}
