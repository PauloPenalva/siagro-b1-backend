using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Cte;

public class CteAccessKeyTests
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
    public void Build_composes_44_digits_with_model_57_and_valid_dv()
    {
        const string prefix = "352610091098480001285700200008239411234567";
        var key = CteAccessKey.Build("35", new DateTime(2026, 10, 22), "09109848000128", 2, 82394, 1, "12345678");

        Assert.Equal(44, key.Length);
        Assert.StartsWith(prefix, key);
        Assert.Equal("57", key.Substring(20, 2));
        Assert.Equal(ReferenceDv(key[..43]).ToString(), key[43..]);
    }

    [Fact]
    public void IsValidNfeKey_rejects_wrong_dv_and_model()
    {
        var key = NfeTestData.SaleAccessKey;
        Assert.True(CteAccessKey.IsValidNfeKey(key));

        var wrongDigit = key[..30] + (key[30] == '9' ? '0' : (char)(key[30] + 1)) + key[31..];
        Assert.False(CteAccessKey.IsValidNfeKey(wrongDigit));

        var model57 = key[..20] + "57" + key[22..43];
        model57 += ReferenceDv(model57).ToString();
        Assert.False(CteAccessKey.IsValidNfeKey(model57));
    }
}
