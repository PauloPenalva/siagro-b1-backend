namespace SiagroB1.Domain.Enums;

/// <summary>
/// Como o total do Frete/Remoção (<c>FreightPrice</c>) sai do valor unitário informado no romaneio
/// ou no ticket de pesagem (GAC-1191).
/// </summary>
public enum FreightCalculationMethod
{
    /// <summary>Valor fixo da viagem: o total é o próprio unitário (Mhagro).</summary>
    Fixed = 0,

    /// <summary>Por saco de 60 kg: peso bruto (KG) / 60 × unitário (Yokotobi, inclusive trigo).</summary>
    PerBag = 1,
}
