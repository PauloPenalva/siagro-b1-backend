using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// A marca "Armazém" (QryGroup23) do PN STANDALONE: é ela que faz o parceiro aparecer como armazém.
/// A inclusão forçava "N" e a edição a ignorava. Corpo sem a marca não pode desmarcar um armazém.
/// </summary>
public class BusinessPartnerWarehouseFlagTests
{
    private static BusinessPartnerService Service(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>());

    private static BusinessPartnerModel Partner(string? flag) => new()
    {
        CardCode = "ARM900", CardName = "ARMAZEM TESTE", CardType = "S", QryGroup23 = flag,
    };

    private static async Task<string?> StoredFlag(UnitOfWork db) =>
        (await db.Context.BusinessPartners.AsNoTracking().SingleAsync(b => b.CardCode == "ARM900")).QryGroup23;

    [Theory]
    [InlineData("Y", "Y")]
    [InlineData("N", "N")]
    [InlineData(null, "N")]
    [InlineData("y", "N")]
    [InlineData("X", "N")]
    public async Task Create_persists_the_warehouse_flag(string? sent, string expected)
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).CreateAsync(Partner(sent));

        Assert.Equal(expected, await StoredFlag(db));
    }

    [Fact]
    public async Task Update_marks_and_unmarks_the_partner_as_warehouse()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner("N"));

        await Service(db).UpdateAsync("ARM900", Partner("Y"));
        Assert.Equal("Y", await StoredFlag(db));

        await Service(db).UpdateAsync("ARM900", Partner("N"));
        Assert.Equal("N", await StoredFlag(db));
    }

    [Fact]
    public async Task Update_without_the_flag_keeps_the_current_one()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner("N"));
        await Service(db).UpdateAsync("ARM900", Partner("Y"));

        await Service(db).UpdateAsync("ARM900", Partner(null));

        Assert.Equal("Y", await StoredFlag(db));
    }

    [Fact]
    public async Task Update_with_an_invalid_flag_stores_no()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner("N"));
        await Service(db).UpdateAsync("ARM900", Partner("Y"));

        await Service(db).UpdateAsync("ARM900", Partner("X"));

        Assert.Equal("N", await StoredFlag(db));
    }
}
