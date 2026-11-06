using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketPricingTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketTradingSettingsServiceTests
{
    private static MarketTradingSettingsInput Input(
        string? fx = null, decimal? volume = null, decimal? deviation = null, string? um = null, string? freightUm = null) =>
        new(fx, volume, deviation, um, freightUm);

    [Fact]
    public async Task Get_NoRow_ReturnsNulls()
    {
        var db = await SeedPricingAsync();

        Assert.Equal(Input(), await SettingsService(db).GetAsync());
    }

    [Fact]
    public async Task Settings_ThresholdChange_RequiresBoardPermission()
    {
        var db = await SeedPricingAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            SettingsService(db, PermissionCodes.MarketManage).SaveAsync(Input(volume: 500m), "ana"));
        await SettingsService(db, PermissionCodes.MarketBoardApprove).SaveAsync(Input(volume: 500m), "ana");

        Assert.Equal(500m, (await db.Context.MarketTradingSettings.AsNoTracking().SingleAsync()).BoardApprovalVolumeTons);
    }

    [Fact]
    public async Task Settings_OtherChange_RequiresManagePermission()
    {
        var db = await SeedPricingAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            SettingsService(db, PermissionCodes.MarketBoardApprove).SaveAsync(Input(um: "kg"), "ana"));
        var saved = await SettingsService(db, PermissionCodes.MarketManage).SaveAsync(Input(um: " kg ", freightUm: "ton"), "ana");

        Assert.Equal("KG", saved.ContractUnitOfMeasureCode);
        Assert.Equal("TON", saved.ContractFreightUmCode);
    }

    [Fact]
    public async Task Settings_UnchangedThreshold_NeedsOnlyManage()
    {
        var db = await SeedPricingAsync();
        await SettingsService(db, PermissionCodes.MarketBoardApprove).SaveAsync(Input(volume: 500m), "ana");

        await SettingsService(db, PermissionCodes.MarketManage).SaveAsync(Input(volume: 500m, um: "KG"), "bia");

        Assert.Equal("KG", (await db.Context.MarketTradingSettings.AsNoTracking().SingleAsync()).ContractUnitOfMeasureCode);
    }

    [Fact]
    public async Task Settings_Validation()
    {
        var db = await SeedPricingAsync(SpotFx(), Brl());
        var service = SettingsService(db, PermissionCodes.MarketManage, PermissionCodes.MarketBoardApprove);

        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input(volume: 0m), "ana"));
        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input(deviation: -1m), "ana"));
        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input(um: "SACAS"), "ana"));
        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input(fx: "NAO EXISTE"), "ana"));
        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input(fx: "CCMF27"), "ana"));
        var saved = await service.SaveAsync(Input(fx: "DOL COM"), "ana");
        Assert.Equal("DOL COM", saved.SpotFxInstrumentCode);
    }

    [Theory]
    [InlineData("SC")]
    [InlineData("TON")]
    public async Task Settings_ContractUnitOtherThanKg_Rejected(string um)
    {
        var db = await SeedPricingAsync();

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            SettingsService(db, PermissionCodes.MarketManage).SaveAsync(Input(um: um, freightUm: "SC"), "ana"));
        Assert.Equal("Nesta versão o contrato gerado pela boleta é sempre em KG.", e.Message);
    }

    [Theory]
    [InlineData(0.0001, null)]
    [InlineData(null, 0.00004)]
    public async Task Settings_ThresholdRoundedToZero_Rejected(double? volume, double? deviation)
    {
        var db = await SeedPricingAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            SettingsService(db, PermissionCodes.MarketBoardApprove).SaveAsync(
                Input(volume: (decimal?)volume, deviation: (decimal?)deviation), "ana"));
        Assert.Empty(db.Context.MarketTradingSettings);
    }
}
