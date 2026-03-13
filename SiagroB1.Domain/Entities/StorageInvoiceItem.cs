using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_INVOICE_ITEMS")]
public class StorageInvoiceItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid StorageInvoiceKey { get; set; }
    public StorageInvoice? StorageInvoice { get; set; }

    public StorageInvoiceItemType ItemType { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Description { get; set; }

    public DateTime ReferenceDate { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(18,8) NOT NULL")]
    public decimal UnitPriceOrRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) NOT NULL")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal TotalQuantityLoss { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? SourceType { get; set; }

    public Guid? SourceKey { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? SourceCode { get; set; }
}