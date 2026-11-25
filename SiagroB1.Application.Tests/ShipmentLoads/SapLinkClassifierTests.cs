using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class SapLinkClassifierTests
{
    private const string Type1Text = "Valor(es) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?";
    private const string Type2Text = "Valor(es) e qtde(s) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?";

    private static SapLinkAssessment Assess(decimal qe, decimal? price, params (int Doc, decimal Qty, decimal Total)[] nfs) =>
        SapLinkClassifier.Assess(qe, price, nfs.Select(n => new SapLinkInvoice(n.Doc, n.Qty, n.Total)).ToList());

    [Fact]
    public void Equal_quantity_and_value_is_normal()
    {
        var a = Assess(30000, 2.00m, (1, 30000, 60000.00m));
        Assert.Equal(SapLinkType.Normal, a.Type);
        Assert.Null(a.Message);
        Assert.Equal(60000.00m, a.EffectiveValue);
    }

    [Fact]
    public void Value_within_one_cent_is_normal() =>
        Assert.Equal(SapLinkType.Normal, Assess(30000, 2.00m, (1, 30000, 60000.01m)).Type);

    [Fact]
    public void Value_beyond_one_cent_is_type_one()
    {
        var a = Assess(30000, 2.00m, (1, 30000, 60000.02m));
        Assert.Equal(SapLinkType.ValueDivergent, a.Type);
        Assert.Equal(Type1Text, a.Message);
    }

    [Fact]
    public void Smaller_invoice_is_type_two()
    {
        var a = Assess(30000, 2.00m, (1, 29800, 59600.00m));
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, a.Type);
        Assert.Equal(200m, a.QuantityDifference);
        Assert.Equal(400.00m, a.ValueDifference);
        Assert.Equal(Type2Text, a.Message);
    }

    [Fact]
    public void Larger_invoice_is_type_two_with_negative_difference()
    {
        var a = Assess(30000, 2.00m, (1, 30150, 60300.00m));
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, a.Type);
        Assert.Equal(-150m, a.QuantityDifference);
    }

    [Fact]
    public void Quantity_differs_but_value_matches_is_still_type_two() =>
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, Assess(30000, 2.00m, (1, 29800, 60000.00m)).Type);

    [Fact]
    public void Without_reference_price_only_quantity_decides()
    {
        var normal = Assess(30000, null, (1, 30000, 1.00m));
        Assert.Equal(SapLinkType.Normal, normal.Type);
        Assert.Null(normal.EffectiveValue);
        Assert.Null(normal.ValueDifference);

        var two = Assess(30000, null, (1, 29800, 1.00m));
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, two.Type);
        Assert.Null(two.EffectiveValue);
        Assert.All(two.Shares, s => Assert.Null(s.EffectiveValue));
    }

    [Fact]
    public void Effective_quantity_is_prorated_and_the_last_share_takes_the_remainder()
    {
        var a = Assess(30000, 2.00m, (1, 10000, 20000m), (2, 19800, 39600m));
        Assert.Equal(10067.114m, a.Shares[0].EffectiveQuantity);
        Assert.Equal(19932.886m, a.Shares[1].EffectiveQuantity);
        Assert.Equal(30000m, a.Shares.Sum(s => s.EffectiveQuantity));
        Assert.Equal(20134.23m, a.Shares[0].EffectiveValue);
        Assert.Equal(39865.77m, a.Shares[1].EffectiveValue);
        Assert.Equal(20000m, a.Shares[0].InvoiceValue);
    }

    [Fact]
    public void Empty_invoices_or_zero_quantity_throw()
    {
        Assert.Throws<ArgumentException>(() => SapLinkClassifier.Assess(10, 1m, []));
        Assert.Throws<ArgumentException>(() => Assess(10, 1m, (1, 0m, 0m)));
    }
}
