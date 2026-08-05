namespace SiagroB1.Domain.Enums;

/// <summary>
/// De onde a liberação de embarque nasceu. Distingue a liberação comum — que
/// autoriza o fornecedor a entregar, e cujo físico ainda está por chegar — da
/// liberação emitida por uma transferência de titularidade, em que a mercadoria
/// já está fisicamente em nosso poder e só falta embarcá-la para faturamento.
/// </summary>
public enum ReleaseOrigin
{
    /// <summary>Liberação comum: o físico ainda não foi entregue.</summary>
    Standard = 0,

    /// <summary>
    /// Emitida por <see cref="Entities.OwnershipTransfer"/>: o físico já foi
    /// entregue e está no lote apontado por
    /// <see cref="Entities.ShipmentRelease.StorageAddressCode"/>.
    /// </summary>
    OwnershipTransfer = 1,
}
