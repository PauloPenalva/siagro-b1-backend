using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>GAC-1185: o EDM é contrato com a tela dos contratos planejados da carga.</summary>
public class ShipmentLoadPlannedContractsEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ShipmentLoadsPlannedPurchaseContracts")]
    [InlineData("ShipmentLoadsPlannedSalesContracts")]
    public void Declares_the_entity_sets(string name)
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet(name));
    }

    [Fact]
    public void ShipmentLoad_exposes_both_navigations()
    {
        var load = (IEdmEntityType)Model().FindDeclaredType(typeof(ShipmentLoad).FullName);

        Assert.NotNull(load.FindProperty(nameof(ShipmentLoad.PlannedPurchaseContracts)));
        Assert.NotNull(load.FindProperty(nameof(ShipmentLoad.PlannedSalesContracts)));
    }

    [Fact]
    public void Contracts_expose_planned_shipment_loads()
    {
        var purchase = (IEdmEntityType)Model().FindDeclaredType(typeof(PurchaseContract).FullName);
        var sales = (IEdmEntityType)Model().FindDeclaredType(typeof(SalesContract).FullName);

        Assert.NotNull(purchase.FindProperty(nameof(PurchaseContract.PlannedShipmentLoads)));
        Assert.NotNull(sales.FindProperty(nameof(SalesContract.PlannedShipmentLoads)));
    }

    [Fact]
    public void Planned_rows_expose_contract_and_load_navigations()
    {
        var purchase = (IEdmEntityType)Model()
            .FindDeclaredType(typeof(ShipmentLoadPlannedPurchaseContract).FullName);
        var sales = (IEdmEntityType)Model()
            .FindDeclaredType(typeof(ShipmentLoadPlannedSalesContract).FullName);

        Assert.NotNull(purchase.FindProperty("Contract"));
        Assert.NotNull(purchase.FindProperty("ShipmentLoad"));
        Assert.NotNull(sales.FindProperty("Contract"));
        Assert.NotNull(sales.FindProperty("ShipmentLoad"));
    }

    /// <summary>Rodada 2: a tela expande a liberação planejada para mostrar item, armazém e saldo.</summary>
    [Fact]
    public void Planned_rows_expose_release_navigations()
    {
        var purchase = (IEdmEntityType)Model()
            .FindDeclaredType(typeof(ShipmentLoadPlannedPurchaseContract).FullName);
        var sales = (IEdmEntityType)Model()
            .FindDeclaredType(typeof(ShipmentLoadPlannedSalesContract).FullName);

        Assert.NotNull(purchase.FindProperty("ShipmentReleaseKey"));
        Assert.NotNull(purchase.FindProperty("ShipmentRelease"));
        Assert.NotNull(sales.FindProperty("SalesShipmentReleaseKey"));
        Assert.NotNull(sales.FindProperty("SalesShipmentRelease"));
    }

    [Theory]
    [InlineData("CC000001", null, null, "CC000001")]
    [InlineData("CC000001", 2, null, "CC000001 (item 2)")]
    [InlineData("CC000001", 2, 500, "CC000001 (item 2) — 500,000")]
    [InlineData("CC000001", null, 500, "CC000001 — 500,000")]
    public void Change_log_describes_the_planned_release(string code, int? rowId, int? quantity, string expected)
    {
        Assert.Equal(expected, ShipmentLoadChangeLogFields.DescribePlannedContract(code, rowId, quantity));
    }
}
