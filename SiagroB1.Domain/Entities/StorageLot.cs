using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_LOTS")]
public class StorageLot : BaseEntity<string>
{
    public required DateTime CreationDate { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("ProcessingCost")]
    public required string ProcessingCostKey { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("WareHouse")]
    public required string WarehouseKey { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    
    [Column(TypeName = "DECIMAL(18, 3) DEFAULT 0")]
    public decimal Balance { get; set; } = 0;
}