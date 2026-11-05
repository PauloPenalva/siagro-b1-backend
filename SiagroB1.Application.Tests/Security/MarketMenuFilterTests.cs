using SiagroB1.Domain.Entities.Common;
using SiagroB1.Security.Services;

namespace SiagroB1.Application.Tests.Security;

public class MarketMenuFilterTests
{
    private static readonly MenuItem[] Items =
    [
        new() { Key = "registers", Title = "Cadastros" },
        new() { Key = "market", Title = "Mercado" },
        new() { Key = "marketQuotesTerminal", Title = "Terminal de Cotações", ParentKey = "market" },
        new() { Key = "marketInstruments", Title = "Instrumentos", ParentKey = "market" },
        new() { Key = "marketCmaSettings", Title = "Integração CMA", ParentKey = "market" },
        new() { Key = "marketing", Title = "Marketing" },
        new() { Key = "marketingPlans", Title = "Planos", ParentKey = "marketing" },
    ];

    private static List<string> Keys(bool enabled) =>
        MarketMenuFilter.Apply(Items, i => i.Key, i => i.ParentKey, enabled).Select(i => i.Key).ToList();

    [Fact]
    public void Enabled_KeepsEverything() => Assert.Equal(Items.Select(i => i.Key), Keys(true));

    [Fact]
    public void Disabled_RemovesMarketTree_KeepsLookalikes() =>
        Assert.Equal(["registers", "marketing", "marketingPlans"], Keys(false));
}
