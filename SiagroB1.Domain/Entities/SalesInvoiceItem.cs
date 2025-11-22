using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }

    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);

    public Guid? SalesContractId { get; set; }
    public virtual SalesContract? SalesContract { get; set; }
}