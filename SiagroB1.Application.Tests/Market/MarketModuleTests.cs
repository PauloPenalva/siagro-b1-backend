using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

public class MarketModuleTests
{
    [Fact]
    public void IsEnabled_ReturnsFalse_WhenKeyMissing() =>
        Assert.False(MarketModule.IsEnabled(TestConfiguration.Empty()));

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenTrue() =>
        Assert.True(MarketModule.IsEnabled(TestConfiguration.With((MarketModule.ConfigKey, "true"))));
}
