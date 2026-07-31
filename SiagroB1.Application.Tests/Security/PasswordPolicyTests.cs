using Microsoft.Extensions.Configuration;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// A política de senha acompanha o padrão do SAP Business One, onde as senhas em uso são curtas e
/// só de dígitos. É uma decisão do cliente, não descuido: exigir mais aqui do que o SAP exige lá
/// deixaria parte dos usuários sem conseguir repetir a senha que já usa.
/// </summary>
public class PasswordPolicyTests
{
    private static PasswordPolicy CreatePolicy(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build());

    [Theory]
    [InlineData("250825")]
    [InlineData("1234")]
    [InlineData("abcd")]
    public void IsValid_ShortPasswordWithoutComplexity_IsAcceptedByDefault(string password)
    {
        Assert.True(CreatePolicy().IsValid(password, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    public void IsValid_BelowTheMinimum_IsRefused(string? password)
    {
        Assert.False(CreatePolicy().IsValid(password, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_HonorsAConfiguredMinimumLength()
    {
        var policy = CreatePolicy(("Security:PasswordPolicy:MinimumLength", "8"));

        Assert.False(policy.IsValid("250825", out _));
        Assert.True(policy.IsValid("25082500", out _));
    }

    [Fact]
    public void IsValid_HonorsTheLetterAndDigitRequirementWhenTurnedOn()
    {
        var policy = CreatePolicy(("Security:PasswordPolicy:RequireLetterAndDigit", "true"));

        Assert.False(policy.IsValid("250825", out _));
        Assert.False(policy.IsValid("abcdef", out _));
        Assert.True(policy.IsValid("abc123", out _));
    }

    /// <summary>
    /// Hash nulo ou vazio é o que marca "usuário ainda sem senha" (criado pelo sync do OUSR). Uma
    /// configuração zerada não pode abrir a porta para senha vazia e apagar essa distinção.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void IsValid_ConfiguredMinimumBelowOne_StillRefusesEmptyPassword(string configured)
    {
        var policy = CreatePolicy(("Security:PasswordPolicy:MinimumLength", configured));

        Assert.Equal(1, policy.MinimumLength);
        Assert.False(policy.IsValid("", out _));
        Assert.True(policy.IsValid("a", out _));
    }

    [Fact]
    public void Description_ReflectsTheRuleInForce()
    {
        Assert.Equal("Mínimo de 4 caracteres.", CreatePolicy().Description);

        Assert.Equal(
            "Mínimo de 8 caracteres, com ao menos uma letra e um número.",
            CreatePolicy(
                ("Security:PasswordPolicy:MinimumLength", "8"),
                ("Security:PasswordPolicy:RequireLetterAndDigit", "true")).Description);
    }
}
