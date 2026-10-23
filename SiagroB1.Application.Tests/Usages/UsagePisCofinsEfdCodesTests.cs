using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>Códigos da EFD Contribuições na natureza: opcionais, validados quando preenchidos.</summary>
public class UsagePisCofinsEfdCodesTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Model(string? revenue, string? creditBase) => new()
    {
        Name = "Venda", Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102", PisCst = "49", CofinsCst = "49",
        RequiresQuantity = true,
        PisCofinsRevenueNatureCode = revenue, PisCofinsCreditBaseCode = creditBase,
    };

    [Fact]
    public async Task Save_AcceptsValidCodes()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Model("401", "18"));

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal("401", read!.PisCofinsRevenueNatureCode);
        Assert.Equal("18", read.PisCofinsCreditBaseCode);
    }

    [Fact]
    public async Task Save_StoresNullWhenEmpty()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Model("  ", ""));

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Null(read!.PisCofinsRevenueNatureCode);
        Assert.Null(read.PisCofinsCreditBaseCode);
    }

    [Theory]
    [InlineData("40")]
    [InlineData("4010")]
    [InlineData("40A")]
    public async Task Save_RejectsRevenueNatureNot3Digits(string revenue)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(TestDb.CreateUnitOfWork(Guid.NewGuid().ToString())).CreateAsync(Model(revenue, null)));

        Assert.Equal("Natureza da receita deve ter 3 dígitos.", ex.Message);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("19")]
    [InlineData("1")]
    [InlineData("AB")]
    public async Task Save_RejectsCreditBaseOutOfRange(string creditBase)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(TestDb.CreateUnitOfWork(Guid.NewGuid().ToString())).CreateAsync(Model(null, creditBase)));

        Assert.Equal("Base de cálculo do crédito deve estar entre 01 e 18.", ex.Message);
    }
}
