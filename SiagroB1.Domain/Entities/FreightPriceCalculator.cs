using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Regra única do total do Frete/Remoção (GAC-1191), usada pelo romaneio e pelo ticket de pesagem.
/// </summary>
/// <remarks>
/// ⚠️ O frontend repete esta conta para mostrar a prévia enquanto o usuário digita
/// (<c>webapp/model/freight.ts</c>). A gravação sempre recalcula aqui — mudou a regra, mude os dois.
/// </remarks>
public static class FreightPriceCalculator
{
    /// <summary>Peso do saco de referência. Os pesos do sistema estão sempre em KG.</summary>
    public const decimal KgPerBag = 60m;

    public static decimal Calculate(FreightCalculationMethod method, decimal unitPrice, decimal grossWeightKg)
        => method switch
        {
            FreightCalculationMethod.PerBag =>
                Math.Round(grossWeightKg / KgPerBag * unitPrice, 2, MidpointRounding.AwayFromZero),
            FreightCalculationMethod.PerTon =>
                Math.Round(grossWeightKg / 1000m * unitPrice, 2, MidpointRounding.AwayFromZero),
            _ => unitPrice,
        };
}
