using Microsoft.Extensions.Configuration;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Configuração mínima com a chave <c>Erp</c>, para exercitar a regra de ativação.</summary>
public static class TaxTestServices
{
    public static IConfiguration Config(string? erp) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp })
            .Build();
}
