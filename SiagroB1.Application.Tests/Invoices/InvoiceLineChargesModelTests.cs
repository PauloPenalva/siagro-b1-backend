using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Invoices;

/// <summary>
/// Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §4): colunas novas nas duas linhas, total geral
/// da linha e do documento (D2) e as propriedades calculadas no EDM.
/// </summary>
public class InvoiceLineChargesModelTests
{
    private static SalesInvoiceItem SalesLine(
        decimal quantity, decimal price, decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = price,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    private static PurchaseInvoiceItem PurchaseLine(
        decimal quantity, decimal price, decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = price,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    [Fact]
    public void Sales_line_grand_total_adds_freight_insurance_and_other_expenses_and_subtracts_the_discount()
    {
        var line = SalesLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m);

        Assert.Equal((1500m, 1600m), (line.Total, line.GrandTotal));
    }

    [Fact]
    public void Purchase_line_grand_total_adds_freight_insurance_and_other_expenses_and_subtracts_the_discount()
    {
        var line = PurchaseLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m);

        Assert.Equal((1500m, 1600m), (line.Total, line.GrandTotal));
    }

    [Fact]
    public void Line_without_charges_keeps_the_grand_total_equal_to_the_products()
    {
        Assert.Equal(60000m, SalesLine(30000m, 2m).GrandTotal);
        Assert.Equal(1500m, PurchaseLine(1000m, 1.5m).GrandTotal);
    }

    [Fact]
    public void Sales_document_totals_sum_the_lines()
    {
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1" };
        invoice.AddItem(SalesLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m));
        invoice.AddItem(SalesLine(10m, 2m, freight: 5m, discount: 1m));

        Assert.Equal((1520m, 105m, 20m, 30m, 51m, 1624m),
            (invoice.TotalInvoiceItems, invoice.TotalFreight, invoice.TotalInsurance, invoice.TotalOtherExpenses,
                invoice.TotalDiscount, invoice.GrandTotal));
    }

    [Fact]
    public void Purchase_document_totals_sum_the_lines()
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        invoice.AddItem(PurchaseLine(1000m, 1.5m, freight: 100m, insurance: 20m, discount: 50m, other: 30m));
        invoice.AddItem(PurchaseLine(10m, 2m, freight: 5m, discount: 1m));

        Assert.Equal((1520m, 105m, 20m, 30m, 51m, 1624m),
            (invoice.TotalInvoiceItems, invoice.TotalFreight, invoice.TotalInsurance, invoice.TotalOtherExpenses,
                invoice.TotalDiscount, invoice.GrandTotal));
    }

    [Fact]
    public async Task Charges_are_persisted_on_both_lines()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1" };
        sale.AddItem(SalesLine(1m, 1m, freight: 1.1m, insurance: 2.2m, discount: 0.3m, other: 4.4m));
        var purchase = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        purchase.AddItem(PurchaseLine(1m, 1m, freight: 1.1m, insurance: 2.2m, discount: 0.3m, other: 4.4m));
        db.Context.SalesInvoices.Add(sale);
        db.Context.PurchaseInvoices.Add(purchase);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var s = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();
        var p = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((1.1m, 2.2m, 0.3m, 4.4m), (s.FreightValue, s.InsuranceValue, s.DiscountValue, s.OtherExpensesValue));
        Assert.Equal((1.1m, 2.2m, 0.3m, 4.4m), (p.FreightValue, p.InsuranceValue, p.DiscountValue, p.OtherExpensesValue));
    }

    [Theory]
    [InlineData("SalesInvoices", "TotalFreight,TotalInsurance,TotalOtherExpenses,TotalDiscount,GrandTotal")]
    [InlineData("SalesInvoicesItems", "FreightValue,InsuranceValue,DiscountValue,OtherExpensesValue,GrandTotal")]
    [InlineData("PurchaseInvoices", "TotalFreight,TotalInsurance,TotalOtherExpenses,TotalDiscount,GrandTotal")]
    [InlineData("PurchaseInvoicesItems", "FreightValue,InsuranceValue,DiscountValue,OtherExpensesValue,GrandTotal")]
    public void Edm_exposes_the_charges_and_the_totals(string entitySet, string properties)
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var type = builder.GetEdmModel().EntityContainer.FindEntitySet(entitySet)!.EntityType;

        foreach (var name in properties.Split(','))
            Assert.Equal("Edm.Decimal", type.FindProperty(name)?.Type.FullName());
    }
}
