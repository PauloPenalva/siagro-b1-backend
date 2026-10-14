using Microsoft.Extensions.Configuration;

namespace SiagroB1.Application.Tests.Support;

public static class TestConfiguration
{
    /// <summary>Configuração mínima com a chave <c>Erp</c> ("SAPB1" ou "STANDALONE").</summary>
    public static IConfiguration Erp(string mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = mode })
            .Build();

    /// <summary>Configuração com chaves avulsas (valor null = chave ausente).</summary>
    public static IConfiguration With(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value))
            .Build();

    /// <summary>Sem chave nenhuma.</summary>
    public static IConfiguration Empty() => With();
}
