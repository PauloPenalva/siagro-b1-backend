using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Mdfe;

public class MdfeAccessKeyTests
{
    /// <summary>DV módulo 11 de referência, escrito à parte do código de produção.</summary>
    private static int ReferenceDv(string digits43)
    {
        var sum = 0;
        var weight = 2;
        for (var i = digits43.Length - 1; i >= 0; i--)
        {
            sum += (digits43[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    [Fact]
    public void Build_uses_model_58()
    {
        var key = MdfeAccessKey.Build("35", new DateTime(2026, 10, 23), "09109848000128", 231, 1, 1, "12345678");

        Assert.Equal(44, key.Length);
        Assert.StartsWith("352610091098480001285823100000000111234567", key);
        Assert.Equal("58", key.Substring(20, 2));
        Assert.Equal(ReferenceDv(key[..43]).ToString(), key[43..]);
    }

    [Fact]
    public void IsValidCteKey_requires_model_57_and_dv()
    {
        var key = MdfeTestData.CteKey1;
        Assert.True(MdfeAccessKey.IsValidCteKey(key));
        Assert.False(MdfeAccessKey.IsValidCteKey(NfeTestData.SaleAccessKey));

        var wrongDv = key[..43] + (key[43] == '9' ? '0' : (char)(key[43] + 1));
        Assert.False(MdfeAccessKey.IsValidCteKey(wrongDv));
    }
}
