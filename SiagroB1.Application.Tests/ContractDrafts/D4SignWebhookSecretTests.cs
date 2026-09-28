using SiagroB1.Web.Hooks;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class D4SignWebhookSecretTests
{
    [Theory]
    [InlineData(null, "qualquer")]      // não configurado: fail-closed
    [InlineData("", "qualquer")]
    [InlineData("SEGREDO", null)]
    [InlineData("SEGREDO", "")]
    [InlineData("SEGREDO", "segredo")]  // diferencia maiúsculas
    [InlineData("SEGREDO", "SEGRED")]   // tamanhos diferentes
    public void Rejects(string? configured, string? received) =>
        Assert.False(D4SignWebhookEndpoint.SecretMatches(configured, received));

    [Fact]
    public void Accepts_the_exact_secret() =>
        Assert.True(D4SignWebhookEndpoint.SecretMatches("SEGREDO", "SEGREDO"));
}
