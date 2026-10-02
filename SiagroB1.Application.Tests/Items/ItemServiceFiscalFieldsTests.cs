using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Items;

/// <summary>Origem e NCM do produto (STANDALONE): opcionais, mas coerentes quando preenchidos.</summary>
public class ItemServiceFiscalFieldsTests
{
    private static ItemService Service(UnitOfWork db) =>
        new(db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"));

    private static ItemModel Soja(byte? origin = 0, string? ncm = "12019000") => new()
    {
        ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", ItmsGrpCod = 105, Enabled = "SIM",
        GoodsOrigin = origin, Ncm = ncm,
    };

    [Fact]
    public async Task Create_and_read_keep_origin_and_ncm()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Soja());

        var read = await Service(db).GetByIdAsync("SOJA");

        Assert.Equal((byte)0, read!.GoodsOrigin);
        Assert.Equal("12019000", read.Ncm);
    }

    [Fact]
    public async Task Update_changes_origin_and_ncm()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Soja());

        await Service(db).UpdateAsync("SOJA", Soja(origin: 1, ncm: "10059010"));
        var read = await Service(db).GetByIdAsync("SOJA");

        Assert.Equal((byte)1, read!.GoodsOrigin);
        Assert.Equal("10059010", read.Ncm);
    }

    [Fact]
    public async Task Both_fields_are_optional()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Soja(origin: null, ncm: null));

        var read = await Service(db).GetByIdAsync("SOJA");
        Assert.Null(read!.GoodsOrigin);
        Assert.Null(read.Ncm);
    }

    [Theory]
    [InlineData("1201900")]
    [InlineData("1201900A")]
    public async Task Ncm_must_have_eight_digits(string ncm)
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(Soja(ncm: ncm)));
    }

    [Fact]
    public async Task Origin_must_be_between_0_and_8()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(Soja(origin: 9)));
    }
}
