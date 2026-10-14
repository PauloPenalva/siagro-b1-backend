using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Spec 2026-10-13 (títulos firmes), "Reversão do sub-projeto 1": a natureza no contrato saiu — o Funrural e a flag
/// "gera financeiro" vêm da linha do documento de entrada conciliado. A grade de impostos digitada NÃO volta a ser
/// editável em STANDALONE, mas o TotalTax volta a ser a soma dela (é o que o SAPB1 sempre usou).
/// </summary>
public class PurchaseContractFiscalComplementRemovalTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("PurchaseContractsGetFiscalComplement")]
    [InlineData("PurchaseContractsSetFiscalComplement")]
    public void The_fiscal_complement_operations_left_the_edm(string operation)
    {
        Assert.DoesNotContain(BuildModel().SchemaElements.OfType<IEdmOperation>(), o => o.Name == operation);
    }

    [Fact]
    public void The_fiscal_complement_table_left_the_context()
    {
        Assert.Null(typeof(AppDbContext).GetProperty("PurchaseContractFiscalComplements"));
    }

    [Fact]
    public async Task Total_tax_is_again_the_sum_of_the_typed_grid()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01", TotalVolume = 1234.567m,
        };
        pc.PriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(), FixationVolume = 1234.567m, FixationPrice = 2.345678m, Status = PriceFixationStatus.Confirmed,
        });
        _db.Context.Taxes.Add(new Tax { Code = "ICMS", Name = "ICMS", Rate = 12m });
        pc.Taxes.Add(new PurchaseContractTax { TaxCode = "ICMS" });
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        var totals = await new PurchaseContractsTotalsService(_db.Context).GetTotals(pc.Key);

        Assert.Equal(2895.90m, totals.TotalPrice);
        Assert.Equal(decimal.Round(2895.90m / 100 * 12m, 2, MidpointRounding.ToEven), totals.TotalTax);
    }
}
