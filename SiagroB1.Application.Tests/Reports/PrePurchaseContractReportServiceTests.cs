using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.PartnerSources;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// O CNPJ do armazém (local de entrega) no pré-contrato vinha da tabela WAREHOUSES — vazia em SAPB1,
/// único modo em que o pré-contrato imprime, e obsoleta em STANDALONE. Agora vem do parceiro.
/// </summary>
public class PrePurchaseContractReportServiceTests
{
    private sealed class StubPartners(Dictionary<string, ReportPartnerDto> partners) : IPartnerSource
    {
        public Task<ReportPartnerDto?> GetByCardCodeAsync(string cardCode, CancellationToken ct = default) =>
            Task.FromResult(partners.GetValueOrDefault(cardCode));
    }

    private static PrePurchaseContractReportService Service(SiagroB1.Infra.UnitOfWork db, IPartnerSource partners)
    {
        var env = new TestWebHostEnvironment(Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot"));
        var configuration = new ConfigurationBuilder().Build();
        return new PrePurchaseContractReportService(
            db, env, partners, configuration,
            new ReportHeaderService(env, configuration, NullLogger<ReportHeaderService>.Instance, FakeCompanyContext.Unresolved()));
    }

    /// <summary>Filial e safra semeadas: o Include de FK obrigatória vira INNER JOIN e esconderia o contrato.</summary>
    private static async Task<(SiagroB1.Infra.UnitOfWork, SiagroB1.Domain.Entities.PurchaseContract)> SeededContract()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new SiagroB1.Domain.Entities.Branch { Code = "01", BranchName = "Matriz", ShortName = "MTZ", TaxId = "98765432000110" });
        db.Context.HarvestSeasons.Add(new SiagroB1.Domain.Entities.HarvestSeason { Code = "25/26", Name = "Safra 2025/2026" });
        var contract = ContractPositionSeed.Purchase();
        db.Context.PurchaseContracts.Add(contract);
        await db.SaveChangesAsync();
        return (db, contract);
    }

    [Theory]
    [InlineData("11222333000181", null, "11222333000181")]
    [InlineData("99999999999999", "11222333000181", "11222333000181")]
    public async Task Warehouse_tax_id_comes_from_the_delivery_location_partner(
        string taxId, string? cnpj, string expected)
    {
        var (db, contract) = await SeededContract();
        var partners = new StubPartners(new()
        {
            ["ARM01"] = new ReportPartnerDto { CardCode = "ARM01", TaxId = taxId, Cnpj = cnpj },
        });

        var data = await Service(db, partners).GetAsync(contract.Key);

        Assert.Equal(expected, data.WarehouseTaxId);
    }

    [Fact]
    public async Task Warehouse_tax_id_is_empty_when_the_partner_is_unknown()
    {
        var (db, contract) = await SeededContract();

        var data = await Service(db, new StubPartners(new())).GetAsync(contract.Key);

        Assert.Null(data.WarehouseTaxId);
    }
}
