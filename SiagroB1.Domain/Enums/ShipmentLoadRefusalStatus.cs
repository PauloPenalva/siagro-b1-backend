namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da recusa de carga em dois tempos (spec 2026-10-09): a filial que emite NF-e pelo Siagro só conclui a
/// recusa com as NF-e de entrada autorizadas. Gravado como int — valores novos sempre no fim.
/// </summary>
public enum ShipmentLoadRefusalStatus
{
    /// <summary>Devoluções criadas, aguardando as NF-e de entrada. A carga fica travada.</summary>
    Pending = 0,

    /// <summary>Todas as devoluções confirmadas e os efeitos do destino aplicados.</summary>
    Completed = 1,

    /// <summary>Cancelada antes de qualquer NF-e autorizada; as devoluções pendentes foram canceladas.</summary>
    Cancelled = 2,
}
