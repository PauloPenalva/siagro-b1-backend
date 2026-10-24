using SiagroB1.Fiscal.Reinf;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfFormatTests
{
    [Fact]
    public void Money_UsesCommaAndTwoDecimals()
    {
        Assert.Equal("1234,50", ReinfFormat.Money(1234.5m));
        Assert.Equal("0,00", ReinfFormat.Money(0m));
    }

    [Fact]
    public void Root_ReturnsFirstEightDigits() =>
        Assert.Equal("12345678", ReinfFormat.Root("12.345.678/0001-90"));

    [Fact]
    public void Digits_StripsNonDigits() =>
        Assert.Equal("12345678000190", ReinfFormat.Digits("12.345.678/0001-90"));
}
