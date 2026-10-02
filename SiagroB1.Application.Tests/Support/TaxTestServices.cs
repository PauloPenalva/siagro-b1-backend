using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Interfaces;
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

    public static SalesInvoicesTaxApplyService Apply(
        UnitOfWork db, IBusinessPartnerService partners, string? erp = "STANDALONE") =>
        new(db, Gate(db, erp), new UsageService(db, NullLogger<UsageService>.Instance), partners,
            new IbsCbsRatesService(db));

    /// <summary>Regra sempre inativa (modo SAPB1) — para os testes antigos, que não exercitam a tributação.</summary>
    public static SalesInvoicesTaxApplyService InactiveApply(UnitOfWork db) =>
        Apply(db, new FakeBusinessPartnerService(), "SAPB1");
}
