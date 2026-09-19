namespace SiagroB1.Application.Services.PurchaseDischarges;

/// <summary>Uma Expedição da carga, já resolvida até o contrato de compra.</summary>
public sealed record DischargeShipment(
    Guid ShippingTransactionKey,
    Guid PurchaseStorageTransactionKey,
    Guid PurchaseContractKey,
    decimal SalesNetWeight,
    bool IsCif);

/// <summary>Parte da diferença do item que cabe a uma Expedição CIF. Negativo = quebra.</summary>
public sealed record DischargeLine(DischargeShipment Shipment, decimal OriginVolume, decimal AdjustmentVolume);

/// <summary>
/// Troca nota (spec §2): rateia a diferença do item conferido entre as Expedições da carga pela
/// participação no peso da carga INTEIRA; só as de contrato CIF viram linha — a parte da FOB é da
/// Yokotobi. O resto do arredondamento vai para a maior participação CIF.
/// </summary>
public static class PurchaseDischargeAllocationPlanner
{
    public static IReadOnlyList<DischargeLine> Plan(
        decimal itemQuantity, decimal itemNetQuantity, IReadOnlyList<DischargeShipment> loadShipments)
    {
        var diff = itemNetQuantity - itemQuantity;
        var totalWeight = loadShipments.Sum(s => s.SalesNetWeight);
        var cif = loadShipments.Where(s => s.IsCif && s.SalesNetWeight > 0).ToList();

        if (diff == 0 || totalWeight <= 0 || cif.Count == 0)
            return [];

        var cifWeight = cif.Sum(s => s.SalesNetWeight);
        var cifDiff = Round(diff * cifWeight / totalWeight);
        var cifOrigin = Round(itemQuantity * cifWeight / totalWeight);

        var lines = cif
            .Select(s => new DischargeLine(
                s,
                Round(itemQuantity * s.SalesNetWeight / totalWeight),
                Round(diff * s.SalesNetWeight / totalWeight)))
            .ToList();

        var largest = lines.MaxBy(l => l.Shipment.SalesNetWeight)!;
        var index = lines.IndexOf(largest);
        lines[index] = largest with
        {
            OriginVolume = largest.OriginVolume + (cifOrigin - lines.Sum(l => l.OriginVolume)),
            AdjustmentVolume = largest.AdjustmentVolume + (cifDiff - lines.Sum(l => l.AdjustmentVolume)),
        };

        return lines.Where(l => l.AdjustmentVolume != 0).ToList();
    }

    private static decimal Round(decimal value) => decimal.Round(value, 3, MidpointRounding.ToEven);
}
