using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

public class StorageStatementReportRowDto
{
    public DateTime? TransactionDate { get; set; }
    public string? TransactionTime { get; set; }
    public string? Code { get; set; }

    public string TransactionTypeName { get; set; } = "";
    public string TransactionStatusName { get; set; } = "";

    public string? CardCode { get; set; }
    public string? CardName { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }

    public string? StorageAddressCode { get; set; }

    public decimal GrossWeight { get; set; }
    public decimal NetWeight { get; set; }

    public decimal QtyEntrada { get; set; }
    public decimal QtySaida { get; set; }
    public decimal RunningBalance { get; set; }

    public decimal Umidade { get; set; }
    public decimal Impurezas { get; set; }
    public decimal Avariados { get; set; }
    public decimal Ardidos { get; set; }
    public decimal PH { get; set; }
    public decimal FN { get; set; }

    public decimal DryingServicePrice { get; set; }
    public decimal CleaningServicePrice { get; set; }
    public decimal ShipmentPrice { get; set; }
    public decimal ReceiptServicePrice { get; set; }

    public decimal TotalServiceValue { get; set; }

    public string? InvoiceNumber { get; set; }
    public string? InvoiceSerie { get; set; }
    public string? ChaveNFe { get; set; }

    public string? Comments { get; set; }

    // 🔹 agrupamento
    public string GroupKey { get; set; } = "";
    public string GroupDescription { get; set; } = "";

    // 🔹 subtotal por grupo (preenchido na última linha do grupo)
    public decimal GroupTotalEntrada { get; set; }
    public decimal GroupTotalSaida { get; set; }
    public decimal GroupTotalServicos { get; set; }

    public bool IsGroupLastRow { get; set; }
}