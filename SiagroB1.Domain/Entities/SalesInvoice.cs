using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES")]
public class SalesInvoice : BaseEntity
{
    [Column(TypeName = "VARCHAR(9)")]
    public string? InvoiceNumber { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(3)")]
    public string? InvoiceSeries { get; set; } = string.Empty;
    
    public DateTime? InvoiceDate { get; set; } = DateTime.Now.Date;
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; } = string.Empty;
    
    public string? Comments { get; set; }

    public ICollection<SalesInvoiceItem> Items { get; set; } = [];
}