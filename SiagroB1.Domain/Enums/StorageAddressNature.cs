namespace SiagroB1.Domain.Enums;

/// <summary>
/// Para que o lote SERVE (GAC-1181 fase 2) — eixo independente de
/// <see cref="StorageOwnershipType"/> (de quem é a mercadoria) e de
/// <see cref="StorageAddressStatus"/> (ciclo de vida).
/// </summary>
public enum StorageAddressNature
{
    /// <summary>Lote comum de armazenagem.</summary>
    Regular = 0,

    /// <summary>
    /// Lote de passagem: recebe a mercadoria descarregada num transbordo e a devolve ao
    /// caminhão. Só existe em armazém próprio, fica fora da Expedição de Grãos comum e fora
    /// da cobrança de armazenagem e da quebra técnica.
    /// </summary>
    Transshipment = 1,
}
