using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES")]
public class SalesInvoice : BaseEntity
{
    [Column(TypeName = "VARCHAR(10)")] 
    public string? DocTypeCode { get; set; }
    public virtual DocType? DocType { get; set; }

    [Column(TypeName = "VARCHAR(9)")] 
    public string? InvoiceNumber { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(3)")] 
    public string? InvoiceSeries { get; set; } = string.Empty;

    public DateTime? InvoiceDate { get; set; } = DateTime.Now.Date;

    public InvoiceStatus? InvoiceStatus { get; set; }
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; } = string.Empty;  
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal NetWeight  { get; set; }
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public string? TruckingCompanyCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string? TruckingCompanyName { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public ICollection<SalesInvoiceItem> Items { get; set; } = [];
    
    [NotMapped]
    public decimal TotalInvoiceItems => Items.Sum(i => i.Total);
}