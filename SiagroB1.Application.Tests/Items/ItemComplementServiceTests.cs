using Microsoft.EntityFrameworkCore;

using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Items;

public class ItemComplementServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ItemComplementService Service() => new(_db);

    [Fact]
    public async Task GetAsync_ReturnsNullWhenItemHasNoComplement()
    {
        Assert.Null(await Service().GetAsync("SOJA"));
    }

    [Fact]
    public async Task SetAsync_CreatesTheComplementOnFirstCall()
    {
        var result = await Service().SetAsync("SOJA", "SC", 60m);

        Assert.Equal("SOJA", result.ItemCode);
        Assert.Equal("SC", result.CommercialUnitOfMeasureCode);
        Assert.Equal(60m, result.CommercialFactor);

        var stored = await Service().GetAsync("SOJA");
        Assert.Equal("SC", stored!.CommercialUnitOfMeasureCode);
        Assert.Equal(60m, stored.CommercialFactor);
    }

    [Fact]
    public async Task SetAsync_UpdatesInPlaceInsteadOfCreatingASecondRow()
    {
        await Service().SetAsync("SOJA", "SC", 60m);
        await Service().SetAsync("SOJA", "TON", 1000m);

        Assert.Equal(1, await _db.Context.ItemComplements.CountAsync(x => x.ItemCode == "SOJA"));

        var stored = await Service().GetAsync("SOJA");
        Assert.Equal("TON", stored!.CommercialUnitOfMeasureCode);
        Assert.Equal(1000m, stored.CommercialFactor);
    }

    [Fact]
    public async Task SetAsync_ClearsTheComplementWhenBothFieldsAreNull()
    {
        await Service().SetAsync("SOJA", "SC", 60m);

        var result = await Service().SetAsync("SOJA", null, null);

        Assert.Null(result.CommercialUnitOfMeasureCode);
        Assert.Null(result.CommercialFactor);
    }

    /// <summary>
    /// Campo vazio do diálogo chega como "" e não como null. Guardar "" faria a UoM parecer
    /// configurada, e o diálogo de faturamento tentaria converter o preço por um fator inexistente.
    /// </summary>
    [Fact]
    public async Task SetAsync_TreatsAnEmptyUnitOfMeasureAsNotConfigured()
    {
        var result = await Service().SetAsync("SOJA", "  ", null);

        Assert.Null(result.CommercialUnitOfMeasureCode);
    }

    [Fact]
    public async Task GetAsync_IsScopedToTheItem()
    {
        await Service().SetAsync("SOJA", "SC", 60m);
        await Service().SetAsync("MILHO", "SC", 50m);

        Assert.Equal(50m, (await Service().GetAsync("MILHO"))!.CommercialFactor);
    }

    [Fact]
    public async Task SetAsync_AcceptsAnItemThatDoesNotExistInTheLocalItemMaster()
    {
        // Em modo SAPB1 o mestre de itens está no OITM, então a tabela não tem FK para ITEMS.
        Assert.Empty(await _db.Context.Items.ToListAsync());

        var result = await Service().SetAsync("ITEM-SO-DO-SAP", "SC", 60m);

        Assert.Equal("ITEM-SO-DO-SAP", result.ItemCode);
    }
}
