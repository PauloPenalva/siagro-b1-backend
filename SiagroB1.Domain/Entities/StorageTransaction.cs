using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_TRANSACTIONS")]
public class StorageTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }
    
    [ForeignKey(nameof(StorageAddress))]
    public required Guid StorageAddressKey { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }
    
    public DateTime? TransactionDate { get; set; } = DateTime.Now.Date;
    
    [Column(TypeName = "VARCHAR(20)")]
    public string? TransactionTime { get; set; } = DateTime.Now.TimeOfDay.ToString();
    
    public StorageTransactionType TransactionType { get; set; }

    public StorageTransactionsStatus TransactionStatus { get; set; } = StorageTransactionsStatus.Waiting;
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal Volume { get; set; }
    
    [ForeignKey(nameof(ShipmentRelease))]
    public Guid? ShipmentReleaseKey  { get; set; }
    public virtual ShipmentRelease? ShipmentRelease { get; set; }
    
    public TransactionCode? TransactionOrigin { get; set; } 
    
    public Guid? ShippingOrderKey { get; set; }
    
    [Column(TypeName = "VARCHAR(11) NOT NULL")]
    public string? TruckDriverCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; }
}