using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Shared;

/// <summary>Peso bruto do CT-e: a linha de quantidade em KG (01) ou TON (02).</summary>
public static class TransportDocumentWeight
{
    /// <summary>Linha de peso bruto do CT-e, ou nulo quando não há quantidade em KG/TON.</summary>
    public static TransportDocumentQuantity? Line(TransportDocument cte) =>
        cte.Quantities.FirstOrDefault(q => q.UnitCode is "01" or "02");

    /// <summary>Converte a linha de peso para kg (TON x 1000).</summary>
    public static decimal ToKg(TransportDocumentQuantity line) => line.UnitCode == "02" ? line.Quantity * 1000m : line.Quantity;

    /// <summary>Peso bruto em kg; 0 quando o CT-e não tem peso.</summary>
    public static decimal GrossKg(TransportDocument cte) => Line(cte) is { } l ? ToKg(l) : 0m;
}
