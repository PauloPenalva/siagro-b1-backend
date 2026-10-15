using Microsoft.EntityFrameworkCore;

using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
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

    [Fact]
    public async Task SetAsync_PersistsControlsRoyalties()
    {
        var result = await Service().SetAsync("SOJA", null, null, controlsRoyalties: true);

        Assert.True(result.ControlsRoyalties);
        Assert.True((await Service().GetAsync("SOJA"))!.ControlsRoyalties);
    }

    [Fact]
    public async Task SetAsync_WithoutControlsRoyalties_KeepsTheCurrentValue()
    {
        await Service().SetAsync("SOJA", null, null, controlsRoyalties: true);

        await Service().SetAsync("SOJA", "SC", 60m);

        Assert.True((await Service().GetAsync("SOJA"))!.ControlsRoyalties);
    }

    [Fact]
    public async Task SetAsync_StoresTheTaxableUnitNormalized()
    {
        var result = await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput(" ton ", 1000m));

        Assert.Equal("TON", result.TaxableUnitOfMeasureCode);
        Assert.Equal(1000m, result.TaxableFactor);
        Assert.Equal("TON", (await Service().GetAsync("SOJA"))!.TaxableUnitOfMeasureCode);
    }

    /// <summary>O diálogo de faturamento grava o complemento sem conhecer a unidade tributável: ela não pode sumir.</summary>
    [Fact]
    public async Task SetAsync_WithoutTheTaxableUnitKeepsTheStoredOne()
    {
        await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TON", 1000m));

        var result = await Service().SetAsync("SOJA", "SC", 60m);

        Assert.Equal("TON", result.TaxableUnitOfMeasureCode);
        Assert.Equal(1000m, result.TaxableFactor);
    }

    [Fact]
    public async Task SetAsync_ClearsTheTaxableUnitWithEmptyValues()
    {
        await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TON", 1000m));

        var result = await Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("  ", null));

        Assert.Null(result.TaxableUnitOfMeasureCode);
        Assert.Null(result.TaxableFactor);
    }

    [Theory]
    [InlineData("TON", null)]
    [InlineData(null, 1000.0)]
    public async Task SetAsync_RejectsHalfOfTheTaxableUnit(string? unit, double? factor)
    {
        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput(unit, (decimal?)factor)));

        Assert.Equal("Informe a unidade tributável e o fator juntos.", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetAsync_RejectsANonPositiveTaxableFactor(double factor)
    {
        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TON", (decimal)factor)));

        Assert.Equal("O fator da unidade tributável deve ser maior que zero.", error.Message);
    }

    [Fact]
    public async Task SetAsync_RejectsATaxableUnitLongerThanTheXmlField()
    {
        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetAsync("SOJA", null, null, taxableUnit: new ItemTaxableUnitInput("TONELADA", 1000m)));

        Assert.Equal("A unidade tributável deve ter no máximo 6 caracteres.", error.Message);
    }
}
