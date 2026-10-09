using Microsoft.Extensions.Configuration;

namespace SiagroB1.Application.Tests.Support;

public static class TestConfiguration
{
    /// <summary>Configuração mínima com a chave <c>Erp</c> ("SAPB1" ou "STANDALONE").</summary>
    public static IConfiguration Erp(string mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = mode })
            .Build();
}
