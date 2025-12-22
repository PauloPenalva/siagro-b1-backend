using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_TRANSACTIONS")]
public class StorageTransaction : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    public Guid? StorageAddressKey { get; set; }
    
    public DateTime? TransactionDate { get; set; } = DateTime.Now.Date;
    
    [Column(TypeName = "VARCHAR(20)")]
    public string? TransactionTime { get; set; } = DateTime.Now.TimeOfDay.ToString();
    
    public StorageTransactionType TransactionType { get; set; }

    public StorageTransactionsStatus TransactionStatus { get; set; } = StorageTransactionsStatus.Pending;
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }
    public virtual UnitOfMeasure? UnitOfMeasure { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal DryingDiscount { get; set; }

    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal CleaningDiscount { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal OthersDicount { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal NetWeight  { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string WarehouseCode { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    
    public Guid? ShipmentReleaseKey  { get; set; }
    public virtual ShipmentRelease? ShipmentRelease { get; set; }
    
    public TransactionCode? TransactionOrigin { get; set; } 
    
    public Guid? ShippingOrderKey { get; set; }
    
    [Column(TypeName = "VARCHAR(11) NULL")]
    public string? TruckDriverCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; }
    
    public Guid? WeighingTicketKey { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? ProcessingCostCode { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }
    
    [Column(TypeName = "VARCHAR(9)")]
    public string? InvoiceNumber { get; set; }
    
    [Column(TypeName = "VARCHAR(3)")]
    public string? InvoiceSerie { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0)")]
    public decimal InvoiceQty { get; set; }
    
    [Column(TypeName = "VARCHAR(44)")]
    public string? ChaveNFe { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0)")]
    public decimal AvaiableVolumeToAllocate { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    public ICollection<StorageTransactionQualityInspection> QualityInspections { get; set; } = [];
    
    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }
}