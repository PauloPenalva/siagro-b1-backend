using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES")]
public class SalesInvoice
{
    [Key]
    public Guid? Key;
    
    public required Guid SalesOrderKey { get; set; }
    public virtual SalesOrder? SalesOrder { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }

    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);

}