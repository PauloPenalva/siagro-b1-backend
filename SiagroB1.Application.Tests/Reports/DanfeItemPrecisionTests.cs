using NFe.Classes.Informacoes.Detalhe;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Casas e modo de unidade do DANFE tirados do XML autorizado (spec 2026-10-14 §1).</summary>
public class DanfeItemPrecisionTests
{
    private static prod Prod(decimal qCom, decimal vUnCom, string uTrib = "KG", decimal? qTrib = null, decimal? vUnTrib = null) => new()
    {
        uCom = "KG", qCom = qCom, vUnCom = vUnCom,
        uTrib = uTrib, qTrib = qTrib ?? qCom, vUnTrib = vUnTrib ?? vUnCom,
    };

    [Fact]
    public void Whole_values_keep_the_minimum_of_two_decimals()
    {
        var precision = DanfeItemPrecision.From([Prod(30000m, 2m)]);

        Assert.Equal(2, precision.QuantityDecimals);
        Assert.Equal(2, precision.UnitPriceDecimals);
        Assert.False(precision.BothUnits);
    }

    /// <summary>XML já autorizado volta do parse com a escala do texto ("30000.0000"): zeros à direita não contam.</summary>
    [Fact]
    public void Trailing_zeros_from_the_xml_do_not_count()
    {
        var precision = DanfeItemPrecision.From([Prod(30000.0000m, 2.0000000000m)]);

        Assert.Equal(2, precision.QuantityDecimals);
        Assert.Equal(2, precision.UnitPriceDecimals);
    }

    [Fact]
    public void Largest_precision_among_all_items_and_both_groups_wins()
    {
        var precision = DanfeItemPrecision.From(
        [
            Prod(100m, 1.5m),
            Prod(30000.5m, 2m, "TON", 30.0005m, 3000.0300003m),
        ]);

        Assert.Equal(4, precision.QuantityDecimals);
        Assert.Equal(7, precision.UnitPriceDecimals);
        Assert.True(precision.BothUnits);
    }

    [Fact]
    public void Precision_is_capped_by_the_layout()
    {
        var precision = DanfeItemPrecision.From([Prod(1.123456m, 1.123456789912m)]);

        Assert.Equal(4, precision.QuantityDecimals);
        Assert.Equal(10, precision.UnitPriceDecimals);
    }

    [Fact]
    public void Same_unit_with_a_different_quantity_still_prints_both()
    {
        Assert.True(DanfeItemPrecision.From([Prod(30000m, 2m, "KG", 30m, 2000m)]).BothUnits);
    }
}
