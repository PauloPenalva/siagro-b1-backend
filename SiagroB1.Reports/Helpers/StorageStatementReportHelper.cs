using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Helpers;

public static class StorageStatementReportHelper
{
    public static string GetTransactionTypeName(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Receipt => "Recebimento",
        StorageTransactionType.Shipment => "Expedição",
        StorageTransactionType.Adjustment => "Ajuste",
        StorageTransactionType.Transfer => "Transferência",
        StorageTransactionType.TechnicalLoss => "Quebra Técnica",
        StorageTransactionType.Processing => "Beneficiamento",
        StorageTransactionType.ShipmentReleased => "Liberação Embarque",
        StorageTransactionType.SalesShipment => "Venda - Saída",
        StorageTransactionType.Purchase => "Compra - Entrada",
        StorageTransactionType.PurchaseReturn => "Compra - Devolução",
        StorageTransactionType.PurchaseQtyComplement => "Compra - Compl. Qtde",
        StorageTransactionType.PurchasePriceComplement => "Compra - Compl. Valor",
        StorageTransactionType.SalesShipmentReturn => "Venda - Devolução",
        _ => "Outros"
    };

    public static string GetTransactionStatusName(StorageTransactionsStatus status) => status switch
    {
        StorageTransactionsStatus.Pending => "Pendente",
        StorageTransactionsStatus.Confirmed => "Confirmado",
        StorageTransactionsStatus.Cancelled => "Cancelado",
        StorageTransactionsStatus.Invoiced => "Faturado",
        _ => "Outros"
    };

    public static bool IsEntry(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Receipt => true,
        StorageTransactionType.Purchase => true,
        StorageTransactionType.PurchaseQtyComplement => true,
        StorageTransactionType.PurchasePriceComplement => true,
        StorageTransactionType.SalesShipmentReturn => true,
        _ => false
    };

    public static bool IsExit(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Shipment => true,
        StorageTransactionType.SalesShipment => true,
        StorageTransactionType.PurchaseReturn => true,
        _ => false
    };

    public static decimal GetSignedQuantity(StorageTransactionType type, decimal qty)
    {
        if (IsEntry(type)) return qty;
        if (IsExit(type)) return -qty;
        return 0;
    }
}