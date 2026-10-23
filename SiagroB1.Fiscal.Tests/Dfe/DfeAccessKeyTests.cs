using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Dfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Dfe;

public class DfeAccessKeyTests
{
    [Fact]
    public void Build_matches_cte_key_for_model_57()
    {
        var issuedAt = new DateTime(2026, 10, 22);
        var cte = CteAccessKey.Build("35", issuedAt, "09109848000128", 2, 82394, 1, "12345678");
        var dfe = DfeAccessKey.Build("35", issuedAt, "09109848000128", "57", 2, 82394, 1, "12345678");

        Assert.Equal(cte, dfe);
        Assert.StartsWith("352610091098480001285700200008239411234567", dfe);
    }

    [Fact]
    public void IsValid_checks_dv_and_model()
    {
        var key = NfeTestData.SaleAccessKey;
        Assert.True(DfeAccessKey.IsValid(key));
        Assert.True(DfeAccessKey.IsValid(key, "55"));
        Assert.False(DfeAccessKey.IsValid(key, "57"));

        var wrongDigit = key[..30] + (key[30] == '9' ? '0' : (char)(key[30] + 1)) + key[31..];
        Assert.False(DfeAccessKey.IsValid(wrongDigit));
        Assert.False(DfeAccessKey.IsValid(key[..43]));
    }

    [Fact]
    public void Text_clean_collapses_and_trims()
    {
        Assert.Equal("A B", DfeText.Clean("  A \t B "));
        Assert.Null(DfeText.CleanOrNull("   "));
        Assert.Equal("AB", DfeText.Clean("ABC", 2));
    }
}
