using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_LOTS")]
public class StorageLot : BaseEntity
{
    public required DateTime CreationDate { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [ForeignKey("ProcessingCost")]
    public required Guid ProcessingCostKey { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }
    
    [ForeignKey("WareHouse")]
    public required Guid WarehouseKey { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    
    [Column(TypeName = "DECIMAL(18, 3) DEFAULT 0")]
    public decimal Balance { get; set; } = 0;
}