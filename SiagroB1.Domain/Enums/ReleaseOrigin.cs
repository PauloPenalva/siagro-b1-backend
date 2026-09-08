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

    /// <summary>
    /// Emitida por uma devolução de mercadoria a um armazém — o retorno de um
    /// documento de saída ou a recusa de uma carga, ambos com destino físico
    /// "volta para armazém". O grão já está no armazém (creditado pelo romaneio
    /// <see cref="StorageTransactionType.SalesShipmentReturn"/>) e esta liberação
    /// é a porta de saída dele: sem ela a mercadoria devolvida não aparece na
    /// Expedição de Grãos e fica presa.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Não consome o contrato de compra.</b> O volume desta liberação já foi
    /// debitado do contrato quando a mercadoria saiu pela primeira vez; contá-lo de
    /// novo duplicaria o liberado. Por isso
    /// <see cref="Entities.ShipmentRelease.ConsumedQuantity"/> e
    /// <see cref="Entities.ShipmentRelease.ReturnedToContractQuantity"/> valem zero
    /// nesta origem — é a única em que o invariante
    /// <c>Consumed + Returned = Released</c> não vale.
    /// </remarks>
    SalesReturn = 2,
}
