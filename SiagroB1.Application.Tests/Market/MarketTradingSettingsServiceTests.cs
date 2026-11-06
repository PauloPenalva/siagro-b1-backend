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
            SettingsService(db, PermissionCodes.MarketBoardApprove).SaveAsync(Input(um: "sc"), "ana"));
        var saved = await SettingsService(db, PermissionCodes.MarketManage).SaveAsync(Input(um: " sc ", freightUm: "ton"), "ana");

        Assert.Equal("SC", saved.ContractUnitOfMeasureCode);
        Assert.Equal("TON", saved.ContractFreightUmCode);
    }

    [Fact]
    public async Task Settings_UnchangedThreshold_NeedsOnlyManage()
    {
        var db = await SeedPricingAsync();
        await SettingsService(db, PermissionCodes.MarketBoardApprove).SaveAsync(Input(volume: 500m), "ana");

        await SettingsService(db, PermissionCodes.MarketManage).SaveAsync(Input(volume: 500m, um: "SC"), "bia");

        Assert.Equal("SC", (await db.Context.MarketTradingSettings.AsNoTracking().SingleAsync()).ContractUnitOfMeasureCode);
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
}
