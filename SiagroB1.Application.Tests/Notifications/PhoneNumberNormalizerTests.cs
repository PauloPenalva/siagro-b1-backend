using SiagroB1.Application.Services.Notifications;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// O telefone digitado no cadastro vira o número que vai para o provedor. Errar aqui não gera
/// erro visível: a mensagem simplesmente não chega, e ninguém descobre.
/// </summary>
public class PhoneNumberNormalizerTests
{
    [Theory]
    // Como o usuário digita, com máscara.
    [InlineData("(66) 99999-8888", "5566999998888")]
    [InlineData("66 99999-8888", "5566999998888")]
    [InlineData("66999998888", "5566999998888")]
    // Fixo de 8 dígitos.
    [InlineData("(66) 3333-4444", "556633334444")]
    public void ToE164Br_AddsCountryCodeToLocalNumber(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.ToE164Br(raw));
    }

    [Theory]
    [InlineData("5566999998888", "5566999998888")]
    [InlineData("556633334444", "556633334444")]
    [InlineData("+55 (66) 99999-8888", "5566999998888")]
    public void ToE164Br_KeepsNumberThatAlreadyHasCountryCode(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.ToE164Br(raw));
    }

    /// <summary>
    /// A armadilha: o DDD 55 (Rio Grande do Sul) é igual ao DDI do Brasil. Um "(55) 99999-8888"
    /// tem 11 dígitos e PRECISA receber o 55 do país mesmo já começando com 55 — tratar como
    /// "já normalizado" mandaria a mensagem para um número de 11 dígitos inexistente.
    /// </summary>
    [Fact]
    public void ToE164Br_AreaCode55_DoesNotSwallowCountryCode()
    {
        Assert.Equal("5555999998888", PhoneNumberNormalizer.ToE164Br("(55) 99999-8888"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("99999-8888")]       // 8 dígitos: sem DDD, não dá para adivinhar
    [InlineData("999998888")]        // 9 dígitos: idem
    [InlineData("telefone")]
    [InlineData("12345678901234")]   // 14 dígitos: longo demais
    [InlineData("449999988888")]     // 12 dígitos sem DDI 55
    public void ToE164Br_InvalidInput_ReturnsNull(string? raw)
    {
        Assert.Null(PhoneNumberNormalizer.ToE164Br(raw));
    }
}
