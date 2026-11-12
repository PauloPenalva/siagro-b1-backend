namespace SiagroB1.Domain.Enums;

/// <summary>Motivo da quebra de trajeto de uma carga (GAC-1213).</summary>
/// <remarks>
/// ⚠️ Persistido como <c>int</c>: valor novo entra SEMPRE no fim da numeração.
/// </remarks>
public enum TransitLossReason
{
    /// <summary>Avaria da mercadoria.</summary>
    Damage = 0,

    /// <summary>Acidente com o veículo.</summary>
    Accident = 1,

    /// <summary>Roubo ou furto da carga.</summary>
    Theft = 2,

    /// <summary>Outro motivo.</summary>
    Other = 3,
}
