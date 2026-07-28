namespace SiagroB1.Domain.Enums;

/// <summary>
/// Resultado do envio para UM destinatário, em UMA tentativa.
/// </summary>
public enum NotificationDeliveryStatus
{
    Sent = 0,
    Failed = 1,

    /// <summary>
    /// Não chegou a ser enviado — telefone sem forma normalizada válida, por exemplo. Separado
    /// de <see cref="Failed"/> para não poluir a métrica de falha do provedor.
    /// </summary>
    Skipped = 2,
}
