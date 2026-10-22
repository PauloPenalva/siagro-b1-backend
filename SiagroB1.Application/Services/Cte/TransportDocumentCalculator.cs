using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Cte;

/// <summary>Totais do CT-e: prestação e ICMS. A alíquota e a redução vêm da natureza, nunca digitadas.</summary>
public static class TransportDocumentCalculator
{
    public static void Recalculate(TransportDocument doc)
    {
        doc.ServiceTotal = doc.Components.Sum(c => c.Value);
        doc.AmountToReceive = doc.ServiceTotal;
        doc.IcmsBase = decimal.Round(doc.ServiceTotal * (1 - doc.IcmsReduction / 100m), 2, MidpointRounding.ToEven);
        doc.IcmsValue = decimal.Round(doc.IcmsBase * doc.IcmsRate / 100m, 2, MidpointRounding.ToEven);
    }
}
