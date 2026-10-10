using SiagroB1.Commons.Validation;

namespace SiagroB1.Application.Tests.Royalties;

public class TaxIdValidatorTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("529.982.247-24", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("11.222.333/0001-80", false)]
    [InlineData("123", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_cpf_and_cnpj(string? value, bool expected) =>
        Assert.Equal(expected, TaxIdValidator.IsValid(value));
}
