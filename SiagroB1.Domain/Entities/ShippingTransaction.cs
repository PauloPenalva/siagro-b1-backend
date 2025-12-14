using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SHIPPING_TRANSACTIONS")]
public class ShippingTransaction : BaseEntity
{
    public Guid PurchaseStorageKey { get; set; }
    public virtual StorageTransaction? PurchaseStorageTransaction { get; set; }
    
    public Guid SalesStorageKey { get; set; }
    public virtual StorageTransaction? SalesStorageTransaction { get; set; }
}