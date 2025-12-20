using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SHIPPING_TRANSACTIONS")]
public class ShippingTransaction : BaseEntity
{
    public Guid PurchaseStorageTransactionKey { get; set; }
    public virtual StorageTransaction? PurchaseStorageTransaction { get; set; }
    
    public Guid SalesStorageTransactionKey { get; set; }
    public virtual StorageTransaction? SalesStorageTransaction { get; set; }
}