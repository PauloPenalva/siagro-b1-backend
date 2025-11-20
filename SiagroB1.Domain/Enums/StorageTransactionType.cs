namespace SiagroB1.Domain.Enums;

public enum StorageTransactionType
{
    Receipt,          // Recebimento/Entrada
    Shipment,         // Expedição/Saída
    Adjustment,       // Ajuste
    Transfer,         // Transferência entre endereços
    QualityLoss,      // Quebra técnica
    Processing,       // Beneficiamento
    ShipmentReleased, // Liberação de entrega/compra para embarque.
}