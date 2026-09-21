using SiagroB1.Application.Services.ContractDrafts;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftNumberSpellerTests
{
    [Theory]
    [InlineData(0, "zero real")]
    [InlineData(1, "um real")]
    [InlineData(1.5, "um real e cinquenta centavos")]
    [InlineData(21, "vinte e um reais")]
    [InlineData(100, "cem reais")]
    [InlineData(115, "cento e quinze reais")]
    [InlineData(1000, "mil reais")]
    [InlineData(2500.07, "dois mil e quinhentos reais e sete centavos")]
    [InlineData(1000000, "um milhão de reais")]
    [InlineData(3450000.10, "três milhões, quatrocentos e cinquenta mil reais e dez centavos")]
    public void Spells_brazilian_currency(decimal value, string expected)
    {
        Assert.Equal(expected, ContractDraftNumberSpeller.Currency(value));
    }

    [Fact]
    public void Spells_date_in_portuguese()
    {
        Assert.Equal("21 de setembro de 2026", ContractDraftNumberSpeller.Date(new DateTime(2026, 9, 21)));
    }
}
