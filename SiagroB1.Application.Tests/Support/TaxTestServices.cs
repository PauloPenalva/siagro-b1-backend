using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Configuração mínima com a chave <c>Erp</c>, para exercitar a regra de ativação.</summary>
public static class TaxTestServices
{
    public static IConfiguration Config(string? erp) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp })
            .Build();

    public static TaxCalculationGate Gate(UnitOfWork db, string? erp) => new(db, Config(erp));
}
