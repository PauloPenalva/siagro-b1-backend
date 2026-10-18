using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Items;

/// <summary>Numeração do cadastro de produto em STANDALONE: série Automático/Manual do produto.</summary>
public class ItemNumberingTests
{
    private static readonly Guid SeriesKey = Guid.NewGuid();

    [Fact]
    public async Task Item_Automatic_UsesItemSeries()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        db.Context.Items.Add(new Item { ItemCode = "000001", ItemName = "EXISTENTE" });
        await db.SaveChangesAsync();
        var service = new ItemService(db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"), fake);

        var model = new ItemModel { ItemCode = "", ItemName = "SOJA", DocNumberKey = SeriesKey };
        await service.CreateAsync(model);

        await DocNumberRequestAsserts.AssertRequestAsync(fake, TransactionCode.Item, SeriesKey, "", 50, "000001");
        Assert.Equal("ST-0001", model.ItemCode);
        Assert.True(await db.Context.Items.AnyAsync(i => i.ItemCode == "ST-0001"));
    }

    [Fact]
    public async Task Item_Manual_StoresTrimmedTypedCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        var service = new ItemService(db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"), fake);

        var model = new ItemModel { ItemCode = " SOJA-01 ", ItemName = "SOJA", DocNumberKey = SeriesKey };
        await service.CreateAsync(model);

        Assert.Equal("SOJA-01", model.ItemCode);
        Assert.True(await db.Context.Items.AnyAsync(i => i.ItemCode == "SOJA-01"));
    }

    [Fact]
    public async Task Item_TypedCodeWithoutSeries_KeepsLegacyBehavior()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        var service = new ItemService(db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"), fake);

        await service.CreateAsync(new ItemModel { ItemCode = " MILHO ", ItemName = "MILHO" });

        Assert.Empty(fake.Requests);
        Assert.True(await db.Context.Items.AnyAsync(i => i.ItemCode == "MILHO"));
        await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.DefaultException>(
            () => service.CreateAsync(new ItemModel { ItemCode = "MILHO", ItemName = "MILHO" }));
    }
}
