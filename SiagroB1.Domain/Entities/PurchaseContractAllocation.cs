using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_ALLOCATIONS")]
public class PurchaseContractAllocation : BaseEntity
{
    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }
    
    public Guid StorageTransactionKey { get; set; }
    public virtual StorageTransaction? StorageTransaction { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Volume { get; set; }
}