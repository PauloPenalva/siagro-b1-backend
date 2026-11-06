using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Market;

public class MarketPermissionGuardTests
{
    [Fact]
    public async Task Guard_Throws_WhenPermissionMissing()
    {
        var guard = new MarketPermissionGuard(new FakeUserPermissions(PermissionCodes.MarketTicketApprove));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => guard.EnsureAsync("ana", PermissionCodes.MarketManage));

        Assert.Equal("Você não tem permissão para esta operação de Trading.", ex.Message);
    }

    [Fact]
    public async Task Guard_Passes_WhenPermissionGranted()
    {
        var guard = new MarketPermissionGuard(new FakeUserPermissions(PermissionCodes.MarketManage));

        await guard.EnsureAsync("ana", PermissionCodes.MarketManage);
    }
}
