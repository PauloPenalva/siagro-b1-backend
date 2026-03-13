using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_INVOICES")]
[Index("Code", IsUnique = true)]
public class StorageInvoice : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressCode { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime ClosingDate { get; set; } = DateTime.Now;

    public StorageInvoiceStatus Status { get; set; } = StorageInvoiceStatus.Closed;

    [Column(TypeName = "DECIMAL(18,2) NOT NULL")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal TotalQuantityLoss { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason  { get; set; }

    public ICollection<StorageInvoiceItem> Items { get; set; } = [];
}