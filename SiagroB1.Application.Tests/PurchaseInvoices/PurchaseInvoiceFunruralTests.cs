using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Fotografia do Funrural na linha e totais do cabeçalho (spec 2026-10-09 §3).</summary>
public class PurchaseInvoiceFunruralTests
{
    private static PurchaseInvoiceItem Line(decimal quantity = 1000m) =>
        new() { Key = Guid.NewGuid(), ItemCode = "TRIGO", Quantity = quantity, UnitPrice = 1.5m };

    [Fact]
    public void Apply_writes_base_rates_and_values()
    {
        var line = Line();

        PurchaseInvoiceFunrural.Apply(line, 1.32m, 0.11m, 0.20m);

        Assert.Equal((1500m, 1.32m, 0.11m, 0.20m),
            (line.FunruralBase, line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
        Assert.Equal((19.80m, 1.65m, 3.00m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
        Assert.Equal(24.45m, line.FunruralValue);
    }

    [Fact]
    public void Apply_uses_the_line_grand_total()
    {
        var line = Line();
        line.FreightValue = 100m;
        line.DiscountValue = 600m;

        PurchaseInvoiceFunrural.Apply(line, null, null, 0.20m);

        Assert.Equal(1000m, line.FunruralBase); // 1500 + 100 − 600
        Assert.Equal(2.00m, line.FunruralSenarValue);
    }

    [Fact]
    public void Clear_zeroes_everything()
    {
        var line = Line();
        PurchaseInvoiceFunrural.Apply(line, 1.32m, 0.11m, 0.20m);

        PurchaseInvoiceFunrural.Clear(line);

        Assert.Equal(0m, line.FunruralBase);
        Assert.Equal(0m, line.FunruralInssRate + line.FunruralRatRate + line.FunruralSenarRate);
        Assert.Equal(0m, line.FunruralValue);
    }

    [Fact]
    public void Header_sums_the_withheld_and_derives_the_net_payable()
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F001" };
        var a = Line();
        var b = Line(500m);
        PurchaseInvoiceFunrural.Apply(a, 1.32m, 0.11m, 0.20m); // 24,45 sobre 1.500
        PurchaseInvoiceFunrural.Apply(b, null, null, 0.20m);   // 1,50 sobre 750
        invoice.AddItem(a);
        invoice.AddItem(b);

        Assert.Equal(25.95m, invoice.TotalFunrural);
        Assert.Equal(2250m - 25.95m, invoice.NetPayable);
    }

    [Fact]
    public void Text_is_prefixed_on_the_purchase_return()
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F001", InvoiceType = PurchaseInvoiceType.Return, IsNfeReturn = true };
        var line = Line();
        PurchaseInvoiceFunrural.Apply(line, null, null, 0.20m);
        invoice.AddItem(line);

        Assert.Equal("Estorno - SENAR 0,20% = R$ 3,00", PurchaseInvoiceFunrural.Text(invoice));
    }

    [Fact]
    public void Text_is_null_without_funrural()
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F001" };
        invoice.AddItem(Line());

        Assert.Null(PurchaseInvoiceFunrural.Text(invoice));
    }
}
