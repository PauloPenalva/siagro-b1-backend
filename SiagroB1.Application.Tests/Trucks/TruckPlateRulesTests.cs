using SiagroB1.Application.Services;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Trucks;

public class TruckPlateRulesTests
{
    [Theory]
    [InlineData("ABC1234", "ABC1234")]   // padrão antigo
    [InlineData("ABC1D23", "ABC1D23")]   // Mercosul
    [InlineData("abc1d23", "ABC1D23")]
    [InlineData("CUD 1H57", "CUD1H57")]
    [InlineData("CUD-1H57", "CUD1H57")]
    [InlineData("  cud 1h57 ", "CUD1H57")]
    public void Normalize_AcceptsBrazilianPlatesInAnyTypingForm(string typed, string expected)
    {
        Assert.Equal(expected, TruckPlateRules.Normalize(typed));
    }

    /// <summary>
    /// "CUD 1H5" é exatamente o que a tela gravou no GAC-1190: a placa CUD 1H57 cortada no
    /// 7º caractere. Sem o espaço sobram 6 caracteres, e isso tem de ser recusado.
    /// </summary>
    [Theory]
    [InlineData("CUD 1H5")]
    [InlineData("CUD1H5")]
    [InlineData("ABCD123")]
    [InlineData("AB12345")]
    [InlineData("ABC12345")]
    [InlineData("ABC1DD3")]
    [InlineData("ABC.1234")]
    public void Normalize_RejectsWhatIsNotABrazilianPlate(string typed)
    {
        var ex = Assert.Throws<DefaultException>(() => TruckPlateRules.Normalize(typed));

        Assert.Equal("Placa inválida. Use o formato ABC1234 ou ABC1D23.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_RejectsEmptyPlate(string? typed)
    {
        var ex = Assert.Throws<DefaultException>(() => TruckPlateRules.Normalize(typed));

        Assert.Equal("Informe a placa do veículo.", ex.Message);
    }
}
