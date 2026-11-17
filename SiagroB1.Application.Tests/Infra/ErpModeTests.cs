using Microsoft.Extensions.Configuration;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Infra;

public class ErpModeTests
{
    private static IConfiguration Config(string? erp) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(erp is null ? [] : new Dictionary<string, string?> { ["Erp"] = erp })
            .Build();

    [Theory]
    [InlineData("SAPB1")]
    [InlineData("sapb1")]
    [InlineData("  SapB1 ")]
    public void IsSapB1_is_true_for_the_sap_mode_in_any_case(string value) =>
        Assert.True(ErpMode.IsSapB1(Config(value)));

    [Theory]
    [InlineData(null)]
    [InlineData("STANDALONE")]
    [InlineData("PROTHEUS")]
    public void IsSapB1_is_false_for_other_modes_and_missing_key(string? value) =>
        Assert.False(ErpMode.IsSapB1(Config(value)));

    [Fact]
    public void IsSapB1_is_false_without_configuration() =>
        Assert.False(ErpMode.IsSapB1(null));
}
