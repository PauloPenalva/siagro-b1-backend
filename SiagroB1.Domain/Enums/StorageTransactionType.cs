namespace SiagroB1.Domain.Enums;

public enum StorageTransactionType
{
    Receipt = 0,                 // Recebimento/Entrada
    Shipment = 1,                // Expedição/Saída
    Adjustment = 2,              // Ajuste
    Transfer = 3,                // Transferência entre endereços
    QualityLoss = 4,             // Quebra técnica
    Processing = 5,              // Beneficiamento
    ShipmentReleased = 6,        // Liberação de entrega/compra para embarque.
    SalesShipment = 7,           // Venda - Saída para Venda
    Purchase = 8,                // Compra - Recebimento/Entrada 
    PurchaseReturn = 9,          // Compra - Devolução 
    PurchaseQtyComplement = 10,   // Compra - Complemento de Quantidade
    PurchasePriceComplement = 11, // Compra - Complemento de Valor
    SalesShipmentReturn = 12,     // Venda - Devolução de Venda/Recusa
}