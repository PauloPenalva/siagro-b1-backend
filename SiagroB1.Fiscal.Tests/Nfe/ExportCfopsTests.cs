using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>CFOPs em que o grupo tributável sai na unidade de exportação do produto.</summary>
public class ExportCfopsTests
{
    [Theory]
    [InlineData("7101")]
    [InlineData("7102")]
    [InlineData("7949")]
    [InlineData("5501")]
    [InlineData("6501")]
    [InlineData("5502")]
    [InlineData("6502")]
    [InlineData("5504")]
    [InlineData("6504")]
    [InlineData("5505")]
    [InlineData("6505")]
    [InlineData(" 5504 ")]
    public void Export_and_specific_purpose_cfops_are_export(string cfop) =>
        Assert.True(ExportCfops.Contains(cfop));

    [Theory]
    [InlineData("5102")]
    [InlineData("6102")]
    [InlineData("5905")]
    [InlineData("5503")]
    [InlineData("1501")]
    [InlineData("7")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_cfops_are_not_export(string? cfop) =>
        Assert.False(ExportCfops.Contains(cfop));
}
