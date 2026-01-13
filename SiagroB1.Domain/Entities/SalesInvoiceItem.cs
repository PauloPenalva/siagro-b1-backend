using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES_ITEMS")]
public class SalesInvoiceItem
{
    [Key]
    public Guid? Key { get; set; }
    
    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }
    
    public Guid? SalesInvoiceItemOriginKey { get; set; }
    public SalesInvoiceItem? SalesInvoiceItemOrigin { get; set; }
    
    [NotMapped]
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);
    
    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal DeliveredQuantity { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal QuantityLoss { get; set; }
    
    [NotMapped]
    public decimal NetQuantity => DeliveredQuantity -  QuantityLoss;

    [NotMapped] 
    public decimal NetTotal => NetQuantity * UnitPrice; 
    
    public SalesInvoiceDeliveryStatus DeliveryStatus { get; set; } = SalesInvoiceDeliveryStatus.Open;
}