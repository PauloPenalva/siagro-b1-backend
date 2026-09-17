namespace SiagroB1.Domain.Enums;

/// <summary>
/// Natureza da carga (GAC-1175). Escolhido na CRIAÇÃO e imutável depois: o tipo decide que
/// documento a carga aceita e se ela tem faturamento, e trocá-lo com documentos já vinculados
/// deixaria a carga com uma composição que o próprio tipo proíbe.
/// </summary>
/// <remarks>
/// ⚠️ Persistido como <c>int</c>, com <c>Normal = 0</c> de propósito: é o valor que a migration
/// dá às cargas que já existem, todas de expedição. Valor novo entra sempre no fim — ver o mesmo
/// aviso em <see cref="ShipmentLoadStatus"/>.
/// </remarks>
public enum ShipmentLoadType
{
    /// <summary>
    /// Expedição: vincula romaneios de embarque (<c>SalesShipment</c>) e termina em faturamento.
    /// É todo o comportamento que a carga tinha antes do GAC-1175.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Remoção: vincula Entradas em Armazenagem (<c>StorageEntryTransaction</c>) e NUNCA fatura —
    /// existe para dar documento ao frete pago para retirar mercadoria de um armazém. Termina em
    /// <see cref="ShipmentLoadStatus.Completed"/>, por ação do usuário.
    /// </summary>
    Removal = 1
}
