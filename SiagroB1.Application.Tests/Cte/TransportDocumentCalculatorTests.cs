using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Cte;

public class TransportDocumentCalculatorTests
{
    private static TransportDocument Doc(decimal rate = 0, decimal reduction = 0, params decimal[] values)
    {
        var doc = new TransportDocument { BranchCode = "01", IcmsRate = rate, IcmsReduction = reduction };
        foreach (var v in values)
            doc.Components.Add(new TransportDocumentComponent { Name = "Frete", Value = v });
        return doc;
    }

    [Fact]
    public void Totals_sum_components()
    {
        var doc = Doc(0, 0, 650m, 50m);

        TransportDocumentCalculator.Recalculate(doc);

        Assert.Equal(700m, doc.ServiceTotal);
        Assert.Equal(700m, doc.AmountToReceive);
    }

    [Fact]
    public void Icms_12_percent_on_650_is_78()
    {
        var doc = Doc(12m, 0, 650m);

        TransportDocumentCalculator.Recalculate(doc);

        Assert.Equal(650m, doc.IcmsBase);
        Assert.Equal(78m, doc.IcmsValue);
    }

    [Fact]
    public void Reduction_applies_to_base()
    {
        var doc = Doc(12m, 40m, 1000m);

        TransportDocumentCalculator.Recalculate(doc);

        Assert.Equal(600m, doc.IcmsBase);
        Assert.Equal(72m, doc.IcmsValue);
    }

    [Fact]
    public void New_document_is_draft_tracked_and_own_transport()
    {
        var doc = new TransportDocument { BranchCode = "01" };

        Assert.Equal(CteStatus.Draft, doc.Status);
        Assert.True(doc.Tracked);
        Assert.True(doc.OwnTransport);
        Assert.Null(doc.Number);
    }
}
