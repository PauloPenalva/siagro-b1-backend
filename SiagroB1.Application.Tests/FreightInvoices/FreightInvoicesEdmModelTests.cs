using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.FreightInvoices;

public class FreightInvoicesEdmModelTests
{
    private static IEdmModel Build()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Model_exposes_freight_invoices_with_items()
    {
        var type = Build().EntityContainer.FindEntitySet("FreightInvoices")!.EntityType;
        Assert.Contains(type.DeclaredKey!, k => k.Name == "Key");
        Assert.NotNull(type.FindProperty("Items"));
    }

    [Fact]
    public void Model_exposes_eligible_ctes_function()
    {
        var model = Build();
        var function = model.SchemaElements.OfType<IEdmFunction>().SingleOrDefault(f => f.Name == "FreightInvoiceEligibleCtes");
        Assert.NotNull(function);
        foreach (var p in new[] { "BranchCode", "TakerCardCode", "From", "To" })
            Assert.Contains(function.Parameters, x => x.Name == p);
        Assert.True(function.ReturnType.IsCollection());
        Assert.Contains("FreightInvoiceEligibleCte", function.ReturnType.Definition.FullTypeName());
        Assert.NotNull(model.EntityContainer.FindOperationImports("FreightInvoiceEligibleCtes").FirstOrDefault());
    }

    [Fact]
    public void Save_model_binds_guid_collection()
    {
        var model = Build();
        var type = (IEdmComplexType)model.FindDeclaredType(typeof(FreightInvoiceSaveRequest).FullName!.Replace(
            typeof(FreightInvoiceSaveRequest).Namespace!, "SIAGROB1"))!;
        Assert.True(type.FindProperty("TransportDocumentKeys")!.Type.IsCollection());
    }

    [Theory]
    [InlineData("FreightInvoicesSave", new[] { "Model" }, "FreightInvoice")]
    [InlineData("FreightInvoicesIssue", new[] { "Key" }, null)]
    [InlineData("FreightInvoicesCancel", new[] { "Key" }, null)]
    [InlineData("FreightInvoicesBatchCreate", new[] { "BranchCode", "From", "To", "TakerCardCode", "Issue" }, "FreightInvoiceBatchResult")]
    public void Model_exposes_freight_invoice_actions(string name, string[] parameters, string? returns)
    {
        var model = Build();
        var action = model.SchemaElements.OfType<IEdmAction>().SingleOrDefault(a => a.Name == name);
        Assert.NotNull(action);
        Assert.NotNull(model.EntityContainer.FindOperationImports(name).FirstOrDefault());
        foreach (var p in parameters) Assert.Contains(action.Parameters, x => x.Name == p);
        if (returns is null) Assert.Null(action.ReturnType);
        else Assert.Contains(returns, action.ReturnType.Definition.FullTypeName());
    }
}
